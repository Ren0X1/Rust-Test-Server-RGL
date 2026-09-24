using HarmonyLib;
using Oxide.Core.Libraries.Covalence;
using Oxide.Core.Plugins;

namespace Oxide.Plugins
{
    [Info("CreativeSetup", "local", "1.3.0")]
    [Description("Blueprints desbloqueados y workbench nivel 3 siempre, mediodia fijo y permisos")]
    class CreativeSetup : RustPlugin
    {
        // Permisos que se conceden solos al grupo "default" (servidor local, solo tu)
        static readonly string[] Permisos = {
            "skins.use", "skins.admin",
            "buildingskins.use", "buildingskins.all", "buildingskins.build",
            "buildingskins.tc", "buildingskins.admin",
            "workshopskinviewer.use",
            "godmode.admin", "godmode.toggle", "godmode.invulnerable",
            "godmode.untiring", "godmode.lootplayers", "godmode.lootprotection",
            "vanish.allow", "vanish.unlock", "vanish.damage",
            "vanish.teleport", "vanish.invviewer"
        };

        void ConcederPermisos()
        {
            int n = 0;
            foreach (var p in Permisos)
            {
                if (!permission.PermissionExists(p)) { PrintWarning("Permiso no registrado (se ignora): " + p); continue; }
                if (permission.GroupHasPermission("default", p)) continue;
                permission.GrantGroupPermission("default", p, null);
                n++;
            }
            Puts("Permisos concedidos al grupo default: " + n + " nuevos.");
        }

        void OnServerInitialized()
        {
            // server.cfg se ejecuta antes de cargar el mundo y el sistema de
            // entorno resetea la hora, asi que la fijamos aqui.
            timer.Once(5f, FijarMediodia);
            timer.Once(8f, ConcederPermisos);

            // Workbench 3 y blueprints para todo el mundo, siempre
            foreach (var p in BasePlayer.activePlayerList) Preparar(p);
        }

        // ─────────────────────────────────────────────────────────────
        //  Workbench 3 permanente
        //
        //  El nivel de mesa sale de BasePlayer.currentCraftLevel, que vale 0
        //  en cuanto no estas dentro del trigger de un banco. Con eso:
        //   - PlayerMetabolism.UpdateWorkbenchFlags repinta los flags del
        //     jugador cada poco (de ahi que aparecieran y desaparecieran), y
        //   - PlayerBlueprints.CanCraft compara ese nivel con el que pide el
        //     blueprint, asi que el servidor tambien rechazaba el crafteo.
        //  Por eso se parchea el getter: siempre nivel 3, estes donde estes.
        // ─────────────────────────────────────────────────────────────
        [HarmonyPatch(typeof(BasePlayer), "currentCraftLevel", MethodType.Getter), AutoPatch]
        static class ParcheNivelMesa
        {
            static void Postfix(ref float __result) { __result = 3f; }
        }

        static void PonerBanco(BasePlayer p)
        {
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, true);
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, true);
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, true);
        }

        // ─────────────────────────────────────────────────────────────
        //  Modo creativo de verdad, el del cliente
        //
        //  creative.allusers solo vale para el servidor: IsInCreativeMode es
        //  "allUsers || flag del jugador", asi que con allusers el servidor ni
        //  pone el flag ni avisa al cliente, y el cliente sigue pidiendo
        //  materiales para encender el boton Craft.
        //  creative.togglecreativemodeuser si hace las dos cosas: el flag (que
        //  se replica) y el comando debug.setcreative_ui, que es el que pone
        //  el inventario en modo creativo. Se replica aqui a mano.
        // ─────────────────────────────────────────────────────────────
        static void PonerCreativo(BasePlayer p)
        {
            p.SetPlayerFlag(BasePlayer.PlayerFlags.CreativeMode, true);
            p.Command("debug.setcreative_ui", true);
        }

        void Preparar(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            PonerCreativo(player);
            PonerBanco(player);
            UnlockAll(player);
        }

        void OnPlayerRespawned(BasePlayer player) => timer.Once(1f, () => Preparar(player));

        // Para comprobar desde la consola o el panel que el parche esta puesto
        [ConsoleCommand("creativo.estado")]
        void CcEstado(ConsoleSystem.Arg arg)
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                var flags = (p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench1) ? "1" : "-")
                          + (p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench2) ? "2" : "-")
                          + (p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench3) ? "3" : "-");
                Puts(p.displayName + ": nivel de mesa " + p.currentCraftLevel + " · flags " + flags
                     + " · creativo " + (p.HasPlayerFlag(BasePlayer.PlayerFlags.CreativeMode) ? "SI" : "NO"));
            }
            if (BasePlayer.activePlayerList.Count == 0) Puts("No hay nadie conectado.");
        }

        [ChatCommand("banco")]
        void CmdBanco(BasePlayer player, string cmd, string[] args)
        {
            Preparar(player);
            player.ChatMessage("<color=#8cf>[Creativo]</color> Workbench nivel 3 y todos los blueprints puestos.");
        }

        void FijarMediodia()
        {
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "env.progresstime", "false");
            ConsoleSystem.Run(ConsoleSystem.Option.Server, "env.time", "12");
            Puts("Hora fijada a mediodia permanente.");
        }

        [ChatCommand("dia")]
        void CmdDia(BasePlayer player, string cmd, string[] args)
        {
            FijarMediodia();
            player.ChatMessage("<color=#8cf>[Creativo]</color> Mediodia permanente activado.");
        }

        void OnPlayerConnected(BasePlayer player)
        {
            if (player == null) return;
            timer.Once(3f, () => Preparar(player));
        }

        void UnlockAll(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            try
            {
                player.blueprints.UnlockAll();
                player.ChatMessage("<color=#8cf>[Creativo]</color> Todos los blueprints desbloqueados. Usa <color=#ff0>/skin</color> con un item en la mano.");
                Puts("Blueprints desbloqueados para " + player.displayName);
            }
            catch (System.Exception e)
            {
                PrintWarning("No se pudo desbloquear via API (" + e.Message + "), probando por consola...");
                ConsoleSystem.Run(ConsoleSystem.Option.Server, "inventory.unlockall", player.UserIDString);
            }
        }

        [ChatCommand("unlockall")]
        void CmdUnlockAll(BasePlayer player, string cmd, string[] args) => UnlockAll(player);
    }
}
