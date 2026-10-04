package dev.skycraft.client;

import java.nio.file.Files;
import java.nio.file.Path;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/** Opt-in local regression fixture; inaccessible without -Dlethalcraft.verify=true. */
final class RegressionChecks {
    private static BlockPos chest;
    static void run(Minecraft mc, int check) {
        try {
            if (check >= 60 && check <= 69) { moons(mc, check); return; }
            if (check >= 40 && check <= 59) { combat(mc, check); return; }
            if (check >= 30 && check <= 38) { multiplayer(mc, check); return; }
            if (check >= 20 && check <= 29) {
                var server = mc.getSingleplayerServer(); var uuid = mc.player.getUUID();
                server.execute(() -> {
                    try {
                        var player = server.getPlayerList().getPlayer(uuid);
                        var pos = new BlockPos(12, 4, 12);
                        if (check == 20 || check == 22) {
                            player.level().setBlock(pos, (check == 20 ? Blocks.DIAMOND_BLOCK : Blocks.EMERALD_BLOCK).defaultBlockState(), 3);
                            player.getInventory().setItem(0, new net.minecraft.world.item.ItemStack(check == 20 ? net.minecraft.world.item.Items.DIAMOND : net.minecraft.world.item.Items.EMERALD, check == 20 ? 13 : 29));
                        }
                        var stack = player.getInventory().getItem(0);
                        String report = "world=" + server.getWorldPath(net.minecraft.world.level.storage.LevelResource.ROOT).normalize().getFileName()
                            + "\nblock=" + player.level().getBlockState(pos).getBlock()
                            + "\nitem=" + stack.getItem() + "\ncount=" + stack.getCount() + "\n";
                        var folder = Path.of(System.getProperty("lethalcraft.testOutput"));
                        Files.createDirectories(folder); Files.writeString(folder.resolve("slot-" + check + ".txt"), report);
                    } catch (Exception e) { dev.skycraft.SkyCraft.LOG.error("Slot fixture failed", e); }
                });
                return;
            }
            if (check == 2 || check == 7) {
                var server = mc.getSingleplayerServer();
                var uuid = mc.player.getUUID();
                server.execute(() -> server.getPlayerList().getPlayer(uuid).setGameMode(check == 2 ? GameType.CREATIVE : GameType.SURVIVAL));
                return;
            }
            if (check == 3) {
                var screen = mc.gui.screen();
                var method = screen.getClass().getDeclaredMethod("selectTab", net.minecraft.world.item.CreativeModeTab.class);
                method.setAccessible(true);
                method.invoke(screen, net.minecraft.world.item.CreativeModeTabs.searchTab());
                for (var child : screen.children()) if (child instanceof EditBox box) { box.setFocused(true); screen.setFocused(box); }
                return;
            }
            if (check == 5) {
                chest = mc.player.blockPosition().offset(2, 0, 0);
                var server = mc.getSingleplayerServer();
                var uuid = mc.player.getUUID();
                server.execute(() -> server.getPlayerList().getPlayer(uuid).level().setBlock(chest, Blocks.CHEST.defaultBlockState(), 3));
                return;
            }
            if (check == 6) {
                mc.hitResult = new BlockHitResult(Vec3.atCenterOf(chest), Direction.UP, chest, false);
                ContextInteraction.interact(mc);
                return;
            }
            if (check == 9) {
                var server = mc.getSingleplayerServer(); var uuid = mc.player.getUUID();
                server.execute(() -> server.getPlayerList().getPlayer(uuid).level().setBlock(chest, Blocks.AIR.defaultBlockState(), 3));
                return;
            }
            StringBuilder report = new StringBuilder();
            var screen = mc.gui.screen();
            report.append("screen=").append(screen == null ? "none" : screen.getClass().getSimpleName()).append('\n');
            if (screen != null) for (var child : screen.children()) if (child instanceof EditBox box) report.append("text=").append(box.getValue()).append('\n');
            var folder = Path.of(System.getProperty("lethalcraft.testOutput"));
            Files.createDirectories(folder);
            Files.writeString(folder.resolve("minecraft-check-" + check + ".txt"), report.toString());
        } catch (Exception e) { dev.skycraft.SkyCraft.LOG.error("LethalCraft regression check " + check + " failed", e); }
    }
    private static void combat(Minecraft mc,int check) {
        var server=mc.getSingleplayerServer(); var uuid=mc.player.getUUID();
        server.execute(()->{
            try {
                var p=server.getPlayerList().getPlayer(uuid);var level=p.level();
                if(check==45) for(var guest:server.getPlayerList().getPlayers()) {
                    guest.stopUsingItem();guest.setGameMode(dev.skycraft.net.SkyNet.isHost(guest)?GameType.CREATIVE:GameType.SURVIVAL);
                    guest.setHealth(20);guest.damageCooldownTime=0;guest.getFoodData().setFoodLevel(16);
                    guest.getInventory().clearContent();guest.setItemSlot(net.minecraft.world.entity.EquipmentSlot.OFFHAND,new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.SHIELD));
                }
                if(check==46) for(var guest:server.getPlayerList().getPlayers()) if(!dev.skycraft.net.SkyNet.isHost(guest)) {
                    var tnt=new net.minecraft.world.entity.item.PrimedTnt(level,guest.getX(),guest.getY()+.5,guest.getZ()+6,null);
                    tnt.setNoGravity(true);tnt.setDeltaMovement(Vec3.ZERO);tnt.setFuse(2);level.addFreshEntity(tnt);
                }
                if(check==40||check==41||check==43) {
                    p.stopUsingItem();p.setGameMode(check==41?GameType.CREATIVE:GameType.SURVIVAL);
                    p.setHealth(20);p.damageCooldownTime=0;p.getFoodData().setFoodLevel(16);
                    p.getInventory().clearContent();
                    p.setItemSlot(net.minecraft.world.entity.EquipmentSlot.OFFHAND,new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.SHIELD));
                    p.setYRot(0);p.setXRot(0);
                }
                if(check==42||check==44) {
                    // Real primed TNT ticks and explodes through vanilla ServerExplosion.
                    var tnt=new net.minecraft.world.entity.item.PrimedTnt(level,p.getX(),p.getY()+.5,p.getZ()+(check==42?2:6),null);
                    tnt.setNoGravity(true);tnt.setDeltaMovement(Vec3.ZERO);tnt.setFuse(2);level.addFreshEntity(tnt);
                }
                if(check>=50) {
                    var folder=Path.of(System.getProperty("lethalcraft.testOutput"));Files.createDirectories(folder);
                    Files.writeString(folder.resolve("combat-"+check+".txt"),"health="+p.getHealth()+"\nblocking="+p.isBlocking()+"\nshieldDamage="+p.getOffhandItem().getDamageValue()+"\n");
                    if(check==59)for(var guest:server.getPlayerList().getPlayers())if(!dev.skycraft.net.SkyNet.isHost(guest))
                        Files.writeString(folder.resolve("guest-combat.txt"),"health="+guest.getHealth()+"\nshieldDamage="+guest.getOffhandItem().getDamageValue()+"\n");
                }
            } catch(Exception e){dev.skycraft.SkyCraft.LOG.error("Combat fixture failed",e);}
        });
    }
    private static void multiplayer(Minecraft mc, int check) throws Exception {
        var marker = new BlockPos(-1, 1, -14);
        if (check == 30) {
            var server = mc.getSingleplayerServer();
            server.execute(() -> {
                var level = dev.skycraft.world.MoonWorlds.active(server);
                for (int x=-8;x<=4;x++) for (int z=-20;z<=-7;z++) level.setBlock(new BlockPos(x,0,z), Blocks.STONE.defaultBlockState(),3);
                level.setBlock(marker,Blocks.DIAMOND_BLOCK.defaultBlockState(),3);
                for (var p : server.getPlayerList().getPlayers()) {
                    boolean host = dev.skycraft.net.SkyNet.isHost(p);
                    p.setGameMode(GameType.CREATIVE);
                    p.getInventory().setItem(0,new net.minecraft.world.item.ItemStack(host ? net.minecraft.world.item.Items.DIAMOND : net.minecraft.world.item.Items.EMERALD_BLOCK,host?13:23));
                }
                multiplayerReport(mc,check);
            }); return;
        }
        if (check == 32) { mc.gameMode.startDestroyBlock(marker,Direction.UP); return; }
        if (check == 33) {
            mc.gameMode.useItemOn(mc.player,net.minecraft.world.InteractionHand.MAIN_HAND,new BlockHitResult(Vec3.atCenterOf(marker.below()).add(0,.5,0),Direction.UP,marker.below(),false)); return;
        }
        if (check == 37) {
            // Simulate a dropped MC connection while the native lobby remains connected.
            mc.getConnection().getConnection().disconnect(net.minecraft.network.chat.Component.literal("LethalCraft reconnect fixture")); return;
        }
        var server=mc.getSingleplayerServer();
        if (server!=null) server.execute(()->multiplayerReport(mc,check)); else multiplayerReport(mc,check);
    }
    private static void multiplayerReport(Minecraft mc,int check) {
        try {
            var server=mc.getSingleplayerServer(); var level=server==null?mc.level:dev.skycraft.world.MoonWorlds.active(server);
            var text=new StringBuilder("integrated=").append(server!=null).append("\nplayers=").append(level.players().size())
                .append("\nmarker=").append(level.getBlockState(new BlockPos(-1,1,-14)).getBlock()).append('\n');
            for (var p:level.players()) {
                var item=p.getInventory().getItem(0);
                text.append("player=").append(p.getPlainTextName()).append(" uuid=").append(p.getUUID()).append(" item=").append(item.getItem()).append(" count=").append(item.getCount()).append(" position=").append(p.position()).append('\n');
                if(server==null)text.append("visible=").append(MultiplayerClient.draw(p)).append('\n');
            }
            if (server!=null) text.append("world=").append(server.getWorldPath(net.minecraft.world.level.storage.LevelResource.ROOT)).append('\n');
            var folder=Path.of(System.getProperty("lethalcraft.testOutput"));Files.createDirectories(folder);Files.writeString(folder.resolve("mc-"+check+".txt"),text.toString());
        } catch(Exception e) {dev.skycraft.SkyCraft.LOG.error("Multiplayer fixture failed",e);}
    }

    private static void moons(Minecraft mc, int check) throws Exception {
        var server=mc.getSingleplayerServer();
        if(server==null) {
            var folder=Path.of(System.getProperty("lethalcraft.testOutput"));Files.createDirectories(folder);
            Files.writeString(folder.resolve("moon-"+check+".txt"),"dimension="+mc.level.dimension().identifier()+"\nmoon="+SkyClient.sky().moonId+"\nblock="+mc.level.getBlockState(new BlockPos(12,4,12)).getBlock()+"\ncount="+mc.player.getInventory().getItem(0).getCount()+"\n");
            return;
        }
        server.execute(()->{
            try {
                var level=dev.skycraft.world.MoonWorlds.active(server);
                var block=new BlockPos(12,4,12);var box=block.east();
                if(check==60||check==62) {
                    level.setBlock(block,(check==60?Blocks.DIAMOND_BLOCK:Blocks.EMERALD_BLOCK).defaultBlockState(),3);
                    level.setBlock(box,Blocks.CHEST.defaultBlockState(),3);
                    ((net.minecraft.world.Container)level.getBlockEntity(box)).setItem(0,new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.APPLE,check==60?11:19));
                    if(check==60)for(var player:server.getPlayerList().getPlayers()) {
                        player.setGameMode(GameType.CREATIVE);
                        player.getInventory().setItem(0,new net.minecraft.world.item.ItemStack(net.minecraft.world.item.Items.DIAMOND,dev.skycraft.net.SkyNet.isHost(player)?13:23));
                    }
                }
                var text=new StringBuilder("dimension=").append(level.dimension().identifier()).append("\nblock=").append(level.getBlockState(block).getBlock()).append('\n');
                text.append("oldMarker=").append(level.getBlockState(new BlockPos(-1,1,-14)).getBlock()).append('\n');
                text.append("registeredMoonDimensions=").append(java.util.stream.StreamSupport.stream(server.getAllLevels().spliterator(),false).filter(l->l.dimension().identifier().toString().startsWith("skycraft:moon_")).count()).append('\n');
                if(level.getBlockEntity(box) instanceof net.minecraft.world.Container container)text.append("chestCount=").append(container.getItem(0).getCount()).append('\n');
                for(var player:server.getPlayerList().getPlayers())text.append(dev.skycraft.net.SkyNet.isHost(player)?"hostCount=":"guestCount=").append(player.getInventory().getItem(0).getCount()).append(" dimension=").append(player.level().dimension().identifier()).append('\n');
                var folder=Path.of(System.getProperty("lethalcraft.testOutput"));Files.createDirectories(folder);Files.writeString(folder.resolve("moon-"+check+".txt"),text.toString());
            }catch(Exception e){dev.skycraft.SkyCraft.LOG.error("Moon fixture failed",e);}
        });
    }
}
