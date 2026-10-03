package dev.skycraft.client.mixin;

import net.minecraft.client.server.IntegratedServer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Four-player authenticated Minecraft server, reachable only through the native lobby relay. */
@Mixin(IntegratedServer.class)
public abstract class IntegratedServerMixin {
	@com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation(method = "publishServer(Lnet/minecraft/server/MinecraftServer$MultiplayerScope;I)Z",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/server/network/ServerConnectionListener;startTcpServerListener(Ljava/net/InetAddress;I)V"))
	private void lethalcraft$loopback(net.minecraft.server.network.ServerConnectionListener listener, java.net.InetAddress address, int port,
		com.llamalad7.mixinextras.injector.wrapoperation.Operation<Void> original) throws java.io.IOException {
		original.call(listener, dev.skycraft.client.SkyClient.linked() ? java.net.InetAddress.getLoopbackAddress() : address, port);
	}
	@com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation(method = "publishServer(Lnet/minecraft/server/MinecraftServer$MultiplayerScope;I)Z",
		at = @At(value = "INVOKE", target = "Lnet/minecraft/client/server/LanServerPinger;start()V"))
	private void lethalcraft$noLanBroadcast(net.minecraft.client.server.LanServerPinger pinger,
		com.llamalad7.mixinextras.injector.wrapoperation.Operation<Void> original) {
		if (!dev.skycraft.client.SkyClient.linked()) original.call(pinger);
	}
	@Inject(method = "getMaxPlayers", at = @At("HEAD"), cancellable = true)
	private void skycraft$morePlayers(CallbackInfoReturnable<Integer> cir) {
		cir.setReturnValue(4);
	}
}
