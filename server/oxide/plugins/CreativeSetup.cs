using Oxide.Core.Libraries.Covalence;

namespace Oxide.Plugins
{
    [Info("CreativeSetup", "local", "1.0.1")]
    [Description("Desbloquea todos los blueprints al entrar, para poder craftear cualquier cosa")]
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
            timer.Once(3f, () => UnlockAll(player));
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
