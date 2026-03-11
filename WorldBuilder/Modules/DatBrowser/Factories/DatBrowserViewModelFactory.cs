using Microsoft.Extensions.DependencyInjection;
using WorldBuilder.Modules.DatBrowser.ViewModels;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.Factories {
    public class DatBrowserViewModelFactory : IDatBrowserViewModelFactory {
        private readonly IServiceProvider _serviceProvider;

        public DatBrowserViewModelFactory(IServiceProvider serviceProvider) {
            _serviceProvider = serviceProvider;
        }

        public IterationBrowserViewModel CreateIterationBrowser() {
            return _serviceProvider.GetRequiredService<IterationBrowserViewModel>();
        }

        public GfxObjBrowserViewModel CreateGfxObjBrowser() {
            return _serviceProvider.GetRequiredService<GfxObjBrowserViewModel>();
        }

        public SetupBrowserViewModel CreateSetupBrowser() {
            return _serviceProvider.GetRequiredService<SetupBrowserViewModel>();
        }

        public AnimationBrowserViewModel CreateAnimationBrowser() {
            return _serviceProvider.GetRequiredService<AnimationBrowserViewModel>();
        }

        public PaletteBrowserViewModel CreatePaletteBrowser() {
            return _serviceProvider.GetRequiredService<PaletteBrowserViewModel>();
        }

        public SurfaceTextureBrowserViewModel CreateSurfaceTextureBrowser() {
            return _serviceProvider.GetRequiredService<SurfaceTextureBrowserViewModel>();
        }

        public RenderSurfaceBrowserViewModel CreateRenderSurfaceBrowser() {
            return _serviceProvider.GetRequiredService<RenderSurfaceBrowserViewModel>();
        }

        public SurfaceBrowserViewModel CreateSurfaceBrowser() {
            return _serviceProvider.GetRequiredService<SurfaceBrowserViewModel>();
        }

        public MotionTableBrowserViewModel CreateMotionTableBrowser() {
            return _serviceProvider.GetRequiredService<MotionTableBrowserViewModel>();
        }

        public WaveBrowserViewModel CreateWaveBrowser() {
            return _serviceProvider.GetRequiredService<WaveBrowserViewModel>();
        }

        public EnvironmentBrowserViewModel CreateEnvironmentBrowser() {
            return _serviceProvider.GetRequiredService<EnvironmentBrowserViewModel>();
        }

        public ChatPoseTableBrowserViewModel CreateChatPoseTableBrowser() {
            return _serviceProvider.GetRequiredService<ChatPoseTableBrowserViewModel>();
        }

        public ObjectHierarchyBrowserViewModel CreateObjectHierarchyBrowser() {
            return _serviceProvider.GetRequiredService<ObjectHierarchyBrowserViewModel>();
        }

        public BadDataTableBrowserViewModel CreateBadDataTableBrowser() {
            return _serviceProvider.GetRequiredService<BadDataTableBrowserViewModel>();
        }

        public TabooTableBrowserViewModel CreateTabooTableBrowser() {
            return _serviceProvider.GetRequiredService<TabooTableBrowserViewModel>();
        }

        public NameFilterTableBrowserViewModel CreateNameFilterTableBrowser() {
            return _serviceProvider.GetRequiredService<NameFilterTableBrowserViewModel>();
        }

        public PalSetBrowserViewModel CreatePalSetBrowser() {
            return _serviceProvider.GetRequiredService<PalSetBrowserViewModel>();
        }

        public ClothingTableBrowserViewModel CreateClothingTableBrowser() {
            return _serviceProvider.GetRequiredService<ClothingTableBrowserViewModel>();
        }

        public GfxObjDegradeInfoBrowserViewModel CreateGfxObjDegradeInfoBrowser() {
            return _serviceProvider.GetRequiredService<GfxObjDegradeInfoBrowserViewModel>();
        }

        public SceneBrowserViewModel CreateSceneBrowser() {
            return _serviceProvider.GetRequiredService<SceneBrowserViewModel>();
        }

        public RegionBrowserViewModel CreateRegionBrowser() {
            return _serviceProvider.GetRequiredService<RegionBrowserViewModel>();
        }

        public MasterInputMapBrowserViewModel CreateMasterInputMapBrowser() {
            return _serviceProvider.GetRequiredService<MasterInputMapBrowserViewModel>();
        }

        public RenderTextureBrowserViewModel CreateRenderTextureBrowser() {
            return _serviceProvider.GetRequiredService<RenderTextureBrowserViewModel>();
        }

        public RenderMaterialBrowserViewModel CreateRenderMaterialBrowser() {
            return _serviceProvider.GetRequiredService<RenderMaterialBrowserViewModel>();
        }

        public MaterialModifierBrowserViewModel CreateMaterialModifierBrowser() {
            return _serviceProvider.GetRequiredService<MaterialModifierBrowserViewModel>();
        }

        public MaterialInstanceBrowserViewModel CreateMaterialInstanceBrowser() {
            return _serviceProvider.GetRequiredService<MaterialInstanceBrowserViewModel>();
        }

        public SoundTableBrowserViewModel CreateSoundTableBrowser() {
            return _serviceProvider.GetRequiredService<SoundTableBrowserViewModel>();
        }

        public EnumMapperBrowserViewModel CreateEnumMapperBrowser() {
            return _serviceProvider.GetRequiredService<EnumMapperBrowserViewModel>();
        }

        public EnumIDMapBrowserViewModel CreateEnumIDMapBrowser() {
            return _serviceProvider.GetRequiredService<EnumIDMapBrowserViewModel>();
        }

        public ActionMapBrowserViewModel CreateActionMapBrowser() {
            return _serviceProvider.GetRequiredService<ActionMapBrowserViewModel>();
        }

        public DualEnumIDMapBrowserViewModel CreateDualEnumIDMapBrowser() {
            return _serviceProvider.GetRequiredService<DualEnumIDMapBrowserViewModel>();
        }

        public LanguageStringBrowserViewModel CreateLanguageStringBrowser() {
            return _serviceProvider.GetRequiredService<LanguageStringBrowserViewModel>();
        }

        public ParticleEmitterBrowserViewModel CreateParticleEmitterBrowser() {
            return _serviceProvider.GetRequiredService<ParticleEmitterBrowserViewModel>();
        }

        public PhysicsScriptBrowserViewModel CreatePhysicsScriptBrowser() {
            return _serviceProvider.GetRequiredService<PhysicsScriptBrowserViewModel>();
        }

        public PhysicsScriptTableBrowserViewModel CreatePhysicsScriptTableBrowser() {
            return _serviceProvider.GetRequiredService<PhysicsScriptTableBrowserViewModel>();
        }

        public MasterPropertyBrowserViewModel CreateMasterPropertyBrowser() {
            return _serviceProvider.GetRequiredService<MasterPropertyBrowserViewModel>();
        }

        public FontBrowserViewModel CreateFontBrowser() {
            return _serviceProvider.GetRequiredService<FontBrowserViewModel>();
        }

        public DBPropertiesBrowserViewModel CreateDBPropertiesBrowser() {
            return _serviceProvider.GetRequiredService<DBPropertiesBrowserViewModel>();
        }

        public CharGenBrowserViewModel CreateCharGenBrowser() {
            return _serviceProvider.GetRequiredService<CharGenBrowserViewModel>();
        }

        public VitalTableBrowserViewModel CreateVitalTableBrowser() {
            return _serviceProvider.GetRequiredService<VitalTableBrowserViewModel>();
        }

        public SkillTableBrowserViewModel CreateSkillTableBrowser() {
            return _serviceProvider.GetRequiredService<SkillTableBrowserViewModel>();
        }

        public SpellTableBrowserViewModel CreateSpellTableBrowser() {
            return _serviceProvider.GetRequiredService<SpellTableBrowserViewModel>();
        }

        public SpellComponentTableBrowserViewModel CreateSpellComponentTableBrowser() {
            return _serviceProvider.GetRequiredService<SpellComponentTableBrowserViewModel>();
        }

        public ExperienceTableBrowserViewModel CreateExperienceTableBrowser() {
            return _serviceProvider.GetRequiredService<ExperienceTableBrowserViewModel>();
        }

        public QualityFilterBrowserViewModel CreateQualityFilterBrowser() {
            return _serviceProvider.GetRequiredService<QualityFilterBrowserViewModel>();
        }

        public CombatTableBrowserViewModel CreateCombatTableBrowser() {
            return _serviceProvider.GetRequiredService<CombatTableBrowserViewModel>();
        }

        public ContractTableBrowserViewModel CreateContractTableBrowser() {
            return _serviceProvider.GetRequiredService<ContractTableBrowserViewModel>();
        }

        public LandBlockBrowserViewModel CreateLandBlockBrowser() {
            return _serviceProvider.GetRequiredService<LandBlockBrowserViewModel>();
        }

        public LandBlockInfoBrowserViewModel CreateLandBlockInfoBrowser() {
            return _serviceProvider.GetRequiredService<LandBlockInfoBrowserViewModel>();
        }

        public EnvCellBrowserViewModel CreateEnvCellBrowser() {
            return _serviceProvider.GetRequiredService<EnvCellBrowserViewModel>();
        }

        public LayoutDescBrowserViewModel CreateLayoutDescBrowser() {
            return _serviceProvider.GetRequiredService<LayoutDescBrowserViewModel>();
        }

        public StringTableBrowserViewModel CreateStringTableBrowser() {
            return _serviceProvider.GetRequiredService<StringTableBrowserViewModel>();
        }

        public LanguageInfoBrowserViewModel CreateLanguageInfoBrowser() {
            return _serviceProvider.GetRequiredService<LanguageInfoBrowserViewModel>();
        }
    }
}
