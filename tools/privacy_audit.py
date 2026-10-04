"""Read-only release audit. Reports locations and categories, never matched secret values.

Inspects ZIP/JAR contents, .NET single-file bundles, managed embedded resources,
and optional complete Git history. This is a release check, not a proof that no
possible secret exists. Uses only Python's standard library; executes no payloads.
"""
import argparse
import hashlib
import io
import json
from pathlib import Path
import re
import struct
import subprocess
import zipfile
import zlib

BUNDLE_SIGNATURE = bytes.fromhex("8b1202b96a612038727b930214d7a03213f5b9e6efae3318ee3b2dce24b36aae")
MAX_FILE = 256 * 1024 * 1024
PATTERNS = {
    "personal build path": re.compile(rb"[A-Za-z]:[\\/]Users[\\/](?!Public\b|Default\b)[^\\/\x00\r\n]{1,80}", re.I),
    "credential or private key": re.compile(rb"(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{40,}|AKIA[A-Z0-9]{16}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)"),
    "assigned token": re.compile(rb'''(?:access_token|refresh_token|client_secret|api_key)["']?\s*[:=]\s*["'][A-Za-z0-9_./+\-=]{24,}["']''', re.I),
}
FORBIDDEN = re.compile(r"(?:^|[/\\!])(?:accounts\.json|\.env(?:\.[^/\\!]+)?|[^/\\!]+\.(?:pdb|dmp|pem|key)|Assembly-CSharp[^/\\!]*\.dll|steam_api64\.dll)$", re.I)


class Audit:
    def __init__(self):
        self.findings = []
        self.seen = set()
        self.counts = {"contents": 0, "zip_entries": 0, "bundle_entries": 0, "managed_resources": 0, "git_blobs": 0}

    def scan(self, name, data, depth=0):
        if FORBIDDEN.search(name):
            self.findings.append({"file": name, "kind": "excluded file type"})
        digest = hashlib.sha256(data).digest()
        if digest in self.seen:
            return
        self.seen.add(digest)
        self.counts["contents"] += 1
        if len(data) > MAX_FILE or depth > 8:
            raise ValueError("Audit limit reached: " + name)
        for encoding, text in (("bytes", data), ("UTF16", data.decode("utf-16le", errors="ignore").encode())):
            for kind, pattern in PATTERNS.items():
                hits = len(pattern.findall(text))
                if hits:
                    self.findings.append({"file": name, "kind": kind, "encoding": encoding, "count": hits})
        if data.startswith(b"PK\x03\x04"):
            with zipfile.ZipFile(io.BytesIO(data)) as archive:
                for entry in archive.infolist():
                    if not entry.is_dir():
                        if entry.file_size > MAX_FILE:
                            raise ValueError("Oversized archive entry")
                        self.counts["zip_entries"] += 1
                        self.scan(name + "!" + entry.filename, archive.read(entry), depth + 1)
        if data.startswith(b"MZ"):
            self.bundle(name, data, depth)
            self.resources(name, data, depth)

    def bundle(self, name, data, depth):
        signature = data.find(BUNDLE_SIGNATURE)
        if signature < 8:
            return
        offset = struct.unpack_from("<q", data, signature - 8)[0]
        if not offset:
            return
        stream = io.BytesIO(data)
        stream.seek(offset)
        major, minor, count = struct.unpack("<III", stream.read(12))
        if major not in (1, 2, 6) or count > 10000:
            raise ValueError("Unsupported bundle header")
        read_string(stream)
        if major >= 2:
            stream.read(40)
        for _ in range(count):
            start, size = struct.unpack("<qq", stream.read(16))
            compressed = struct.unpack("<q", stream.read(8))[0] if major >= 6 else 0
            stream.read(1)
            path = read_string(stream)
            if min(start, size, compressed) < 0 or size > MAX_FILE or start + (compressed or size) > len(data):
                raise ValueError("Invalid bundle entry")
            content = data[start:start + (compressed or size)]
            if compressed:
                content = zlib.decompress(content, -15)
            if len(content) != size:
                raise ValueError("Invalid bundle entry size")
            self.counts["bundle_entries"] += 1
            self.scan(name + "!" + path, content, depth + 1)

    def resources(self, name, data, depth):
        pe = struct.unpack_from("<I", data, 0x3c)[0]
        if data[pe:pe+4] != b"PE\0\0":
            return
        sections = struct.unpack_from("<H", data, pe + 6)[0]
        optional_size = struct.unpack_from("<H", data, pe + 20)[0]
        optional = pe + 24
        magic = struct.unpack_from("<H", data, optional)[0]
        directory = optional + (112 if magic == 0x20b else 96)
        if optional_size < directory - optional + 15 * 8:
            return
        cli = struct.unpack_from("<I", data, directory + 14 * 8)[0]
        if not cli:
            return

        def file_offset(rva):
            for index in range(sections):
                header = optional + optional_size + index * 40
                length, address, raw_size, raw_offset = struct.unpack_from("<IIII", data, header + 8)
                if address <= rva < address + max(length, raw_size):
                    return raw_offset + rva - address
            raise ValueError("Invalid PE resource address")

        rva, size = struct.unpack_from("<II", data, file_offset(cli) + 24)
        if not rva or not size:
            return
        start = file_offset(rva)
        position, end = start, start + size
        # Raw strings in all resources were scanned with the assembly. Locate
        # length-prefixed ZIP resources to inspect compressed installer payloads.
        while (position := data.find(b"PK\x03\x04", position, end)) >= 0:
            length = struct.unpack_from("<I", data, position - 4)[0] if position >= start + 4 else 0
            content = data[position:position+length] if 0 < length <= MAX_FILE and position + length <= end else b""
            if content and zipfile.is_zipfile(io.BytesIO(content)):
                self.counts["managed_resources"] += 1
                self.scan(name + "!resource@" + str(position-start), content, depth + 1)
                position += length
            else:
                position += 4

    def history(self, repository):
        def git(*args, **kwargs):
            return subprocess.run(["git", "-C", str(repository), *args], check=True, stdout=subprocess.PIPE, **kwargs).stdout
        objects = git("rev-list", "--objects", "--all").decode().splitlines()
        names = {line.split(" ", 1)[0]: line.split(" ", 1)[-1] for line in objects}
        raw = io.BytesIO(git("cat-file", "--batch", input=("\n".join(names) + "\n").encode()))
        while header := raw.readline():
            oid, kind, size = header.decode().split()
            data = raw.read(int(size)); raw.read(1)
            if kind == "blob":
                self.counts["git_blobs"] += 1
                self.scan("history:" + oid[:12] + "!" + names[oid], data)
        self.scan("Git author metadata", git("log", "--all", "--format=%an <%ae> %cn <%ce>"))


def read_string(stream):
    length = 0
    for shift in range(0, 35, 7):
        byte = stream.read(1)[0]
        length |= (byte & 127) << shift
        if byte < 128:
            if length > 100000:
                raise ValueError("Invalid string length")
            return stream.read(length).decode("utf-8")
    raise ValueError("Invalid string length")


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("paths", nargs="*")
    parser.add_argument("--git")
    parser.add_argument("--report", required=True)
    args = parser.parse_args()
    audit = Audit()
    if args.git:
        audit.history(Path(args.git))
    for path in args.paths:
        file = Path(path)
        audit.scan(file.name, file.read_bytes())
    report = {"checks": audit.counts, "findings": audit.findings}
    Path(args.report).write_text(json.dumps(report, indent=2), encoding="utf-8")
    print(json.dumps(report, indent=2))
    raise SystemExit(bool(audit.findings))
