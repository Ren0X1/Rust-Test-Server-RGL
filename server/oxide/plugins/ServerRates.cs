using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace Oxide.Plugins
{
    [Info("ServerRates", "local", "1.1.0")]
    [Description("Servidor x3: recoleccion, loot y stacks multiplicados, y el loot de los barriles va directo al inventario")]
    class ServerRates : RustPlugin
    {
        // ─────────────────────────────────────────────────────────────
        //  Configuracion (oxide/config/ServerRates.json)
        // ─────────────────────────────────────────────────────────────
        class Configuracion
        {
            public float Multiplicador = 3f;
            public bool LootDeBarrilesAlInventario = true;

            // Stacks: los recursos base van a StackRecursos, el resto de lo
            // que ya se apila se multiplica (armas, ropa... siguen a 1)
            public int StackRecursos = 100000;
            public float MultiplicadorStacks = 5f;

            // Replace: sin esto Newtonsoft anade la lista del JSON a la de por
            // defecto y se duplicaria en cada recarga
            [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
            public List<string> Recursos = new List<string>
            {
                "wood", "stones", "metal.ore", "metal.fragments", "hq.metal.ore", "metal.refined",
                "sulfur.ore", "sulfur", "charcoal", "gunpowder", "scrap", "cloth", "leather",
                "fat.animal", "bone.fragments", "lowgradefuel", "crude.oil", "plantfiber"
            };
        }

        Configuracion cfg;

        protected override void LoadDefaultConfig() => cfg = new Configuracion();

        protected override void LoadConfig()
        {
            base.LoadConfig();
            try { cfg = Config.ReadObject<Configuracion>(); } catch { cfg = null; }
            if (cfg == null) cfg = new Configuracion();
            cfg.Recursos = (cfg.Recursos ?? new List<string>()).Distinct().ToList();
            SaveConfig();
        }

        protected override void SaveConfig() => Config.WriteObject(cfg, true);

        // ─────────────────────────────────────────────────────────────
        //  Tamano de los stacks
        // ─────────────────────────────────────────────────────────────
        // Se guardan los originales para dejarlo todo como estaba al descargar
        // el plugin (si no, cada recarga volveria a multiplicar).
        readonly Dictionary<ItemDefinition, int> _stacksOriginales = new Dictionary<ItemDefinition, int>();

        void OnServerInitialized()
        {
            var recursos = new HashSet<string>(cfg.Recursos);
            int nRecursos = 0, nOtros = 0;

            foreach (var def in ItemManager.itemList)
            {
                if (def.stackable <= 1) continue;
                _stacksOriginales[def] = def.stackable;

                if (recursos.Contains(def.shortname)) { def.stackable = cfg.StackRecursos; nRecursos++; }
                else { def.stackable = Math.Max(1, (int)Math.Round(def.stackable * cfg.MultiplicadorStacks)); nOtros++; }
            }

            Puts("Stacks: " + nRecursos + " recursos a " + cfg.StackRecursos + ", "
                 + nOtros + " items x" + cfg.MultiplicadorStacks + ".");
        }

        void Unload()
        {
            foreach (var kv in _stacksOriginales)
                if (kv.Key != null) kv.Key.stackable = kv.Value;
        }

        int Mult(int cantidad) => Math.Max(1, (int)Math.Round(cantidad * cfg.Multiplicador));

        void Mult(Item item)
        {
            if (item == null) return;
            item.amount = Mult(item.amount);
        }

        // ─────────────────────────────────────────────────────────────
        //  Recoleccion: arboles, piedras, animales, plantas, cantera...
        // ─────────────────────────────────────────────────────────────
        void OnDispenserGather(ResourceDispenser dispenser, BasePlayer player, Item item) => Mult(item);
        void OnDispenserBonus(ResourceDispenser dispenser, BasePlayer player, Item item) => Mult(item);
        void OnGrowableGathered(GrowableEntity planta, Item item, BasePlayer player) => Mult(item);
        void OnQuarryGather(MiningQuarry cantera, Item item) => Mult(item);
        void OnExcavatorGather(ExcavatorArm excavadora, Item item) => Mult(item);
        void OnSurveyGather(SurveyCharge carga, Item item) => Mult(item);

        // Lo que se recoge del suelo con E (piedras, madera, setas, canamo...)
        void OnCollectiblePickup(CollectibleEntity recogible, BasePlayer player, bool comer)
        {
            if (recogible?.itemList == null) return;
            foreach (var ia in recogible.itemList)
                ia.amount = Mult((int)ia.amount);
        }

        // ─────────────────────────────────────────────────────────────
        //  Loot: barriles, cajas...
        // ─────────────────────────────────────────────────────────────
        // OnLootSpawn salta justo antes de PopulateLoot, asi que se
        // multiplica en el siguiente tick, con la caja ya rellena.
        void OnLootSpawn(LootContainer caja)
        {
            NextTick(() =>
            {
                if (caja == null || caja.IsDestroyed || caja.inventory == null) return;
                foreach (var item in caja.inventory.itemList)
                {
                    if (item.info.stackable <= 1) continue;   // armas, ropa... no se multiplican
                    item.amount = Mult(item.amount);
                    item.MarkDirty();
                }
            });
        }

        // Al romper un barril el loot va directo al inventario en vez de caer
        // al suelo. OnEntityDeath salta antes de OnDied, que es donde se tira.
        void OnEntityDeath(LootContainer caja, HitInfo info)
        {
            if (!cfg.LootDeBarrilesAlInventario || caja?.inventory == null) return;
            var player = info?.InitiatorPlayer;
            if (player == null || player.IsNpc) return;

            foreach (var item in new List<Item>(caja.inventory.itemList))
            {
                item.RemoveFromContainer();
                player.GiveItem(item, BaseEntity.GiveItemReason.PickedUp);
            }
        }
    }
}
