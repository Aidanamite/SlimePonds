using MonomiPark.SlimeRancher.DataModel;
using MonomiPark.SlimeRancher.Regions;
using SRML;
using SRML.Config.Attributes;
using SRML.Console;
using SRML.SR;
using SRML.SR.SaveSystem;
using SRML.Utils.Enum;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using System.Linq;
using HarmonyLib;

namespace SlimePonds
{
    public class Main : ModEntryPoint
    {
        internal static Assembly modAssembly = Assembly.GetExecutingAssembly();
        internal static string modName = $"{modAssembly.GetName().Name}";
        internal static string modDir = $"{System.Environment.CurrentDirectory}\\SRML\\Mods\\{modName}";
        internal static System.Random rand = new System.Random();

        public override void PreLoad()
        {
            HarmonyInstance.PatchAll();
        }
        public override void Load()
        {
            var PlotPurchaseUI = Resources.FindObjectsOfTypeAll<EmptyPlotUI>().Find((x) => !x.name.EndsWith("(Clone)"));
            var PondPrefab = SRSingleton<GameContext>.Instance.LookupDirector.GetPlotPrefab(LandPlot.Id.POND);
            var NewPondPrefab = PondPrefab.CreatePrefabCopy();
            NewPondPrefab.GetComponent<LandPlot>().typeId = Id.POND_SLIME_LANDPLOT;
            NewPondPrefab.AddComponent<SlimePondSpawner>();
            var PondUI = Resources.FindObjectsOfTypeAll<PondUI>().Find((x) => !x.name.EndsWith("(Clone)"));
            var NewPondUI = PondUI.gameObject.CreatePrefabCopy();
            var OldPondUI = NewPondUI.GetComponent<PondUI>();
            PondUI.CopyAllTo(NewPondUI.AddComponent<SlimePondUI>());
            Object.DestroyImmediate(OldPondUI);
            NewPondPrefab.GetComponentInChildren<UIActivator>().uiPrefab = NewPondUI;
            NewPondPrefab.GetComponentInChildren<SplashOnTrigger>().gameObject.AddComponent<DissolveOnEnter>();
            Object.DestroyImmediate(NewPondPrefab.GetComponentInChildren<VacuumableDelauncher>());

            
            LookupRegistry.RegisterLandPlot(NewPondPrefab);
            LandPlotRegistry.RegisterPurchasableLandPlot(new LandPlotRegistry.LandPlotShopEntry()
            {
                cost = PlotPurchaseUI.pond.cost * 3,
                icon = PlotPurchaseUI.pond.icon,
                isUnlocked = () => true,
                mainImg = PlotPurchaseUI.pond.img,
                pediaId = Id.POND_SLIME_PEDIA,
                plot = Id.POND_SLIME_LANDPLOT
            });
            
        }
        public static void Log(string message) => Debug.Log($"[{modName}]: " + message);
        public static void LogWarning(string message) => Debug.LogWarning($"[{modName}]: " + message);
    }

    public static class Id
    {
        public static LandPlot.Id POND_SLIME_LANDPLOT => LandPlotId.POND_SLIME;
        public static PediaDirector.Id POND_SLIME_PEDIA => PediaId.POND_SLIME;
    }
    [EnumHolder]
    public static class LandPlotId
    {
        public static readonly LandPlot.Id POND_SLIME;
    }
    [EnumHolder]
    public static class PediaId
    {
        public static readonly PediaDirector.Id POND_SLIME;
    }

    static class ExtentionMethods
    {
        public static T Find<T>(this T[] array, System.Predicate<T> predicate)
        {
            foreach (var i in array)
                if (predicate.Invoke(i))
                    return i;
            return default(T);
        }
    }

    class SlimePondUI : PondUI
    {
        protected override GameObject CreatePurchaseUI()
        {
            PurchaseUI.Purchasable[] array = new PurchaseUI.Purchasable[] {
            new PurchaseUI.Purchasable(
                MessageUtil.Qualify("ui", "l.clean_pond"),
                demolish.icon,
                demolish.img,
                MessageUtil.Qualify("ui", "m.desc.clean_pond"),
                10,
                null,
                () => {activator.GetComponent<SlimePondSpawner>().ResetModel(); RebuildUI(); },
                () => true,
                () => activator.GetComponent<SlimePondSpawner>().Model.siloAmmo[SiloStorage.StorageType.NON_SLIMES].slots.Length > 0,
                "b.clean"),
            new PurchaseUI.Purchasable(
                MessageUtil.Qualify("ui", "l.demolish_plot"),
                demolish.icon,
                demolish.img,
                MessageUtil.Qualify("ui", "m.desc.demolish_plot"),
                demolish.cost,
                null,
                Demolish,
                () => true,
                () => true,
                "b.demolish")
            };
            return SRSingleton<GameContext>.Instance.UITemplates.CreatePurchaseUI(titleIcon, "t.pond_slime", array, false, Close, false);
        }
    }

    class SlimePondSpawner :SRBehaviour, LandPlotModel.Participant
    {
        public static double SpawnDelay => 3600 * Config.SpawnDelay;
        LandPlotModel model;
        MeshRenderer waterSurface;
        public LandPlotModel Model => model;
        static GameObject SpawnSlime(Identifiable.Id id, RegionRegistry.RegionSetId setId) => InstantiateActor(GameContext.Instance.LookupDirector.GetPrefab(id), setId);
        public void InitModel(LandPlotModel Model)
        {
            Model.collectorNextTime = SceneContext.Instance.TimeDirector.WorldTime();
            if (Model.siloAmmo.ContainsKey(SiloStorage.StorageType.NON_SLIMES))
                Model.siloAmmo[SiloStorage.StorageType.NON_SLIMES].slots = new Ammo.Slot[0];
            else
                Model.siloAmmo.Add(SiloStorage.StorageType.NON_SLIMES, new AmmoModel() { slots = new Ammo.Slot[0] });
        }

        void Awake()
        {
            waterSurface = transform.Find("Water/Water Scaler/Surface").GetComponent<MeshRenderer>();
        }
        public void SetModel(LandPlotModel Model)
        {
            if (System.Environment.StackTrace.Contains("SavedGame.Push"))
                foreach (var ammo in Model.siloAmmo.Values)
                    for (int i = 0; i < ammo.slots.Length; i++)
                        if (ammo.slots[i] != null && ammo.slots[i].id != Identifiable.Id.NONE)
                            try
                            {
                                ammo.slots[i] = new Ammo.Slot(Patch_EnumTranslator.translator.TranslateEnum(EnumTranslator.TranslationMode.FROMTRANSLATED, ammo.slots[i].id), ammo.slots[i].count) { emotions = ammo.slots[i].emotions };
                            }
                            catch { }
            model = Model;
            UpdateWaterColor();
        }
        public void ResetModel()
        {
            InitModel(model);
            UpdateWaterColor();
        }

        void Update()
        {
            var spawnTime = model.collectorNextTime - SceneContext.Instance.TimeDirector.WorldTime() + SpawnDelay;
            if (spawnTime <= 0)
            {
                model.collectorNextTime += SpawnDelay;
                if (Main.rand.NextDouble() < Config.SpawnChance)
                {
                    var spawn = GetRandom(Model.siloAmmo[SiloStorage.StorageType.NON_SLIMES].slots);
                    if (spawn != Identifiable.Id.NONE)
                    {
                        try
                        {
                            var g = SpawnSlime(spawn, GetComponentInParent<Region>().setId);
                            g.transform.position = transform.position + new Vector3((float)Main.rand.NextDouble() * 4 - 2, 1, (float)Main.rand.NextDouble() * 4 - 2);
                            g.GetComponent<Rigidbody>().velocity = Vector3.up * 10;
                            g.GetComponent<Rigidbody>().AddTorque(Vector3.up * 100);
                        } catch
                        {
                            Main.Log("Failed to spawn " + spawn);
                        }
                    }
                }
            }
        }

        public void UpdateWaterColor()
        {
            var c = 0;
            var r = 0d;
            var g = 0d;
            var b = 0d;
            foreach (var s in Model.siloAmmo[SiloStorage.StorageType.NON_SLIMES].slots)
            {
                var color = Color.black;
                try {
                    color = SceneContext.Instance.SlimeAppearanceDirector.GetChosenSlimeAppearance(s.id).ColorPalette.Ammo;
                }
                catch
                {
                    Main.LogWarning($"Failed to fetch slime appearance for {s.id} the slime's data may not be fully registered");
                }
                c += s.count;
                r += color.r * s.count;
                g += color.g * s.count;
                b += color.b * s.count;
            }
            var newColor = Color.white;
            if (c > 0)
            {
                newColor = new Color((float)(r / c), (float)(g / c), (float)(b / c));
                Color.RGBToHSV(newColor, out float h, out float s, out float v);
                newColor = Color.HSVToRGB(h, 1, v);
            }
            var mat = waterSurface.material;
            mat.SetVector("_ColorMultiply", new Vector4(newColor.r * 10, newColor.g * 10, newColor.b * 10, 100));
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, newColor);
            tex.Apply();
            mat.SetTexture("_ColorRamp", tex);
        }

        static Identifiable.Id GetRandom(Ammo.Slot[] slots)
        {
            var t = 0;
            foreach (var s in slots)
                t += s.count;
            t = Main.rand.Next(t);
            foreach (var s in slots)
                if (t - s.count < 0)
                    return s.id;
                else
                    t -= s.count;
            return Identifiable.Id.NONE;
        }
    }

    class DissolveOnEnter : SRBehaviour, LandPlotModel.Participant
    {
        LandPlotModel model;
        void OnTriggerEnter(Collider collider)
        {
            var idenity = collider.GetComponent<Identifiable>();
            var vac = collider.GetComponent<Vacuumable>();
            if (idenity && vac && Identifiable.IsSlime(idenity.id) && !Identifiable.IsLargo(idenity.id) && vac.isLaunched())
            {
                var ammo = model.siloAmmo[SiloStorage.StorageType.NON_SLIMES];
                var slot = ammo.slots.Find((x) => x.id == idenity.id);
                if (slot != null)
                    slot.count++;
                else {
                    var slots = new List<Ammo.Slot>(ammo.slots);
                    slots.Add(new Ammo.Slot(idenity.id, 1));
                    ammo.slots = slots.ToArray();
                }
                GetComponentInParent<SlimePondSpawner>().UpdateWaterColor();
                Destroyer.DestroyActor(collider.gameObject, "DissolveOnEnter.OnTriggerEnter");
            }
        }

        public void InitModel(LandPlotModel Model) { }
        public void SetModel(LandPlotModel Model) => model = Model;
    }

    [ConfigFile("settings")]
    static class Config
    {
        public static readonly double SpawnDelay = 0.2;
        public static readonly double SpawnChance = 0.05;
    }

    [HarmonyPatch(typeof(ResourceBundle), "LoadFromText")]
    class Patch_LoadResources
    {
        static void Postfix(string path, Dictionary<string, string> __result)
        {
            var lang = GameContext.Instance.MessageDirector.GetCultureLang();
            if (path == "pedia")
            {
                if (lang == MessageDirector.Lang.RU)
                {
                    __result["t." + Id.POND_SLIME_PEDIA.ToString().ToLowerInvariant()] = "Слизистый пруд";
                    __result["m.intro." + Id.POND_SLIME_PEDIA.ToString().ToLowerInvariant()] = "Пруд со слаймом внутри";
                }
                else
                {
                    __result["t." + Id.POND_SLIME_PEDIA.ToString().ToLowerInvariant()] = "Slime Pond";
                    __result["m.intro." + Id.POND_SLIME_PEDIA.ToString().ToLowerInvariant()] = "A slimy pond";
                }
            }
            else if (path == "ui")
            {
                if (lang == MessageDirector.Lang.RU)
                {
                    __result["l.clean_pond"] = "Очистить пруд";
                    __result["m.desc.clean_pond"] = "Очищает пруд, дабы убрать загрязнение воды от каждого слайма";
                    __result["b.clean"] = "Очистка";
                }
                else
                {
                    __result["l.clean_pond"] = "Clean Pond";
                    __result["m.desc.clean_pond"] = "Cleans the pond water to remove any slime soluted with the water";
                    __result["b.clean"] = "Clean";
                }
            }
        }
    }

    [HarmonyPatch(typeof(EnumTranslator), "TranslateEnum", typeof(System.Type), typeof (EnumTranslator), typeof(EnumTranslator.TranslationMode), typeof(object))]
    class Patch_EnumTranslator
    {
        public static EnumTranslator translator;
        static void Prefix(System.Type enumType, EnumTranslator translator)
        {
            if (enumType == typeof(Identifiable.Id))
                Patch_EnumTranslator.translator = translator;
        }
    }
}