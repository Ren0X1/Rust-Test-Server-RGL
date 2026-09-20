using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("CreativeSetup", "local", "1.1.0")]
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
            timer.Every(10f, Repasar);
            foreach (var p in BasePlayer.activePlayerList) Preparar(p);
        }

        // ─────────────────────────────────────────────────────────────
        //  Workbench 3 permanente
        //
        //  El nivel de mesa de trabajo lo llevan estos flags del jugador.
        //  En el servidor no los toca nada mas (solo el cliente al entrar y
        //  salir de una mesa), asi que puestos una vez se quedan puestos;
        //  el repaso cada 10 s es por si el cliente los quita al alejarse.
        // ─────────────────────────────────────────────────────────────
        static bool TieneBanco(BasePlayer p)
        {
            return p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench1)
                && p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench2)
                && p.HasPlayerFlag(BasePlayer.PlayerFlags.Workbench3);
        }

        static void PonerBanco(BasePlayer p)
        {
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench1, true);
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench2, true);
            p.SetPlayerFlag(BasePlayer.PlayerFlags.Workbench3, true);
            p.cachedCraftLevel = 3f;
            p.nextCheckTime = UnityEngine.Time.realtimeSinceStartup + 10f;
        }

        void Repasar()
        {
            foreach (var p in BasePlayer.activePlayerList)
            {
                if (p == null || !p.IsConnected || p.IsSleeping()) continue;
                if (!TieneBanco(p)) PonerBanco(p);
            }
        }

        void Preparar(BasePlayer player)
        {
            if (player == null || !player.IsConnected) return;
            PonerBanco(player);
            UnlockAll(player);
        }

        void OnPlayerRespawned(BasePlayer player) => timer.Once(1f, () => Preparar(player));

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
