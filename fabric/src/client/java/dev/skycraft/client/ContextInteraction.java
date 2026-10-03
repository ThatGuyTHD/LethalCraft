package dev.skycraft.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.level.block.*;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.EntityHitResult;

/** E uses a targeted Minecraft mechanism; I always opens the inventory. */
public final class ContextInteraction {
    private ContextInteraction() {}

    public static void inventory(Minecraft mc) {
        if (mc.player != null && mc.gui.screen() == null) {
            InputBridge.releaseAll();
            mc.gui.setScreen(new InventoryScreen(mc.player));
        }
    }

    public static void interact(Minecraft mc) {
        if (mc.player == null || mc.level == null || mc.gameMode == null || mc.gui.screen() != null) return;
        if (mc.hitResult instanceof EntityHitResult hit && !(hit.getEntity() instanceof dev.skycraft.combat.SkyrimActorEntity)) {
            mc.gameMode.interact(mc.player, hit.getEntity(), hit, InteractionHand.MAIN_HAND);
            return;
        }
        if (mc.hitResult instanceof BlockHitResult hit && hit.getType() == HitResult.Type.BLOCK
            && !(hit instanceof dev.skycraft.world.SkyClip.SkyrimHitResult)) {
            var state = mc.level.getBlockState(hit.getBlockPos());
            var block = state.getBlock();
            boolean usable = state.getMenuProvider(mc.level, hit.getBlockPos()) != null
                || block instanceof DoorBlock || block instanceof TrapDoorBlock || block instanceof FenceGateBlock
                || block instanceof ButtonBlock || block instanceof LeverBlock || block instanceof NoteBlock
                || block instanceof RepeaterBlock || block instanceof ComparatorBlock || block instanceof DaylightDetectorBlock
                || block instanceof BedBlock || block instanceof BellBlock || block instanceof RespawnAnchorBlock;
            if (usable) {
                mc.gameMode.useItemOn(mc.player, InteractionHand.MAIN_HAND, hit);
                return;
            }
        }
        inventory(mc);
    }
}
