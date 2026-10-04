package dev.skycraft.client;

import dev.skycraft.combat.SkyCombat;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.rendering.v1.EntityRendererRegistry;
import net.minecraft.client.renderer.entity.NoopRenderer;

public final class SkyCraftClient implements ClientModInitializer {
	@Override
	public void onInitializeClient() {
		dev.skycraft.link.SkyLink.announceRunning();
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.registerGlobalReceiver(dev.skycraft.net.SkyNet.Moon.TYPE, (payload, context) -> MoonClient.accept(payload.moon(), payload.dimension()));
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents.DISCONNECT.register((handler, minecraft) -> MoonClient.clear());
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.registerGlobalReceiver(dev.skycraft.net.SkyNet.Hit.TYPE, (payload, context) -> {
			if (dev.skycraft.link.SkyLink.active()) dev.skycraft.link.SkyLink.pushEvent(dev.skycraft.link.Proto.EV_HIT_ACTOR, payload.actor(), payload.damage(), payload.x(), payload.z(), 0, payload.flags(), payload.weapon());
		});
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.registerGlobalReceiver(dev.skycraft.net.SkyNet.ViewMode.TYPE, (payload, context) -> {
			try { MultiplayerClient.view(java.util.UUID.fromString(payload.player()), payload.active()); } catch (IllegalArgumentException ignored) {}
		});
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayConnectionEvents.DISCONNECT.register((handler, minecraft) -> MultiplayerClient.clear());
		ClientTickEvents.END_CLIENT_TICK.register(SkyClient::clientTick);
		// A guest in a friend's world: dying there kills this player's own Skyrim character.
		net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking.registerGlobalReceiver(dev.skycraft.net.SkyNet.Died.TYPE, (payload, context) -> {
			if (dev.skycraft.link.SkyLink.active()) {
				dev.skycraft.link.SkyLink.pushEvent(dev.skycraft.link.Proto.EV_PLAYER_DIED, payload.attackerFormId(), 0, 0, 0, 0, 0);
			}
		});
		// Skyrim draws the real NPC; its Minecraft stand-in is only a hitbox.
		EntityRendererRegistry.register(SkyCombat.SKYRIM_ACTOR, NoopRenderer::new);
		// Players (client-side movement AND the integrated server's re-check of it) use the smooth
		// triangle collider, never Skyrim's voxels; otherwise the server sees the smooth position
		// dip into a voxel and teleports the player back every few ticks.
		dev.skycraft.world.SkyCollision.setSmoothCollider(e -> e instanceof net.minecraft.world.entity.player.Player && SkyClient.linked());
	}
}
