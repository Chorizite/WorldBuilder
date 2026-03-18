using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using WorldBuilder.ViewModels;
using DatReaderWriter.DBObjs;
using DatReaderWriter.Enums;
using DatReaderWriter.Lib.IO;
using DatReaderWriter.Types;
using DatReaderWriter;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.DependencyInjection;
using System;
using WorldBuilder.Shared.Services;
using WorldBuilder.Services;
using HanumanInstitute.MvvmDialogs;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Lib.Input;
using WorldBuilder.Modules.DatBrowser.Factories;

using CommunityToolkit.Mvvm.Input;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class DatBrowserViewModel : ViewModelBase, IToolModule, IHotkeyHandler, IKeywordSearchViewModel {
        public string Name => "Dat Browser";
        public ViewModelBase ViewModel => this;

        public IEnumerable<DBObjType> DatTypes => System.Enum.GetValues<DBObjType>().Where(t => {
            return t switch {
                DBObjType.Iteration => true,
                DBObjType.GfxObj => true,
                DBObjType.Setup => true,
                DBObjType.Animation => true,
                DBObjType.Palette => true,
                DBObjType.SurfaceTexture => true,
                DBObjType.RenderSurface => true,
                DBObjType.Surface => true,
                DBObjType.MotionTable => true,
                DBObjType.Wave => true,
                DBObjType.Environment => true,
                DBObjType.ChatPoseTable => true,
                DBObjType.ObjectHierarchy => true,
                DBObjType.BadDataTable => true,
                DBObjType.TabooTable => true,
                DBObjType.NameFilterTable => true,
                DBObjType.PalSet => true,
                DBObjType.ClothingTable => true,
                DBObjType.GfxObjDegradeInfo => true,
                DBObjType.Scene => true,
                DBObjType.Region => true,
                DBObjType.MasterInputMap => true,
                DBObjType.RenderTexture => true,
                DBObjType.RenderMaterial => true,
                DBObjType.MaterialModifier => true,
                DBObjType.MaterialInstance => true,
                DBObjType.SoundTable => true,
                DBObjType.EnumMapper => true,
                DBObjType.EnumIDMap => true,
                DBObjType.ActionMap => true,
                DBObjType.DualEnumIDMap => true,
                DBObjType.LanguageString => true,
                DBObjType.ParticleEmitter => true,
                DBObjType.PhysicsScript => true,
                DBObjType.PhysicsScriptTable => true,
                DBObjType.MasterProperty => true,
                DBObjType.Font => true,
                DBObjType.DBProperties => true,
                DBObjType.CharGen => true,
                DBObjType.VitalTable => true,
                DBObjType.SkillTable => true,
                DBObjType.SpellTable => true,
                DBObjType.SpellComponentTable => true,
                DBObjType.ExperienceTable => true,
                DBObjType.QualityFilter => true,
                DBObjType.CombatTable => true,
                DBObjType.ContractTable => true,
                DBObjType.LandBlock => true,
                DBObjType.LandBlockInfo => true,
                DBObjType.EnvCell => true,
                DBObjType.LayoutDesc => true,
                DBObjType.StringTable => true,
                DBObjType.LanguageInfo => true,
                _ => false
            };
        });

        public bool CanBrowse => true;

        [ObservableProperty]
        private DBObjType _selectedType;

        [ObservableProperty]
        private ViewModelBase? _currentBrowser;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsSearchVisible))]
        private IDBObj? _selectedObject;

        public bool IsSearchVisible => SelectedObject == null;

        [ObservableProperty]
        private object? _objectOverview;

        [ObservableProperty]
        private int _selectedPropertiesTabIndex;

        [ObservableProperty]
        private uint _previewFileId;

        [ObservableProperty]
        private ObservableCollection<ReflectionNodeViewModel> _reflectionNodes = new();

        [ObservableProperty]
        private string? _currentKeywordsNames;

        [ObservableProperty]
        private string? _currentKeywordsTags;

        [ObservableProperty]
        private string? _currentKeywordsDescriptions;

        [ObservableProperty]
        private string _keywordsSearchText = string.Empty;

        [ObservableProperty]
        private bool _isKeywordsSearchEnabled = true;

        [ObservableProperty]
        private bool _showKeywordsSearchWarning;

        [ObservableProperty]
        private string _keywordsSearchTooltip = string.Empty;

        [ObservableProperty]
        private SearchType _searchType = SearchType.Hybrid;

        [ObservableProperty]
        private bool _isKeywordsSearching;

        [ObservableProperty]
        private bool _isEmbeddingSearchActive;

        [ObservableProperty]
        private bool _isSearchTypeSelectionEnabled;

        public IEnumerable<SearchType> SearchTypes => System.Enum.GetValues<SearchType>();

        public GridBrowserViewModel? GridBrowser => (CurrentBrowser as IDatBrowserViewModel)?.GridBrowser;

        private bool _isSettingObject;
        private bool _showKeywords;

        public bool ShowKeywords => _showKeywords;


        private readonly IDatBrowserViewModelFactory _viewModelFactory;
        private readonly IDialogService _dialogService;
        private readonly IServiceProvider _serviceProvider;
        private readonly IDatReaderWriter _dats;

        // Cached ViewModels for lazy loading
        private IterationBrowserViewModel? _iterationBrowser;
        private GfxObjBrowserViewModel? _gfxObjBrowser;
        private SetupBrowserViewModel? _setupBrowser;
        private AnimationBrowserViewModel? _animationBrowser;
        private PaletteBrowserViewModel? _paletteBrowser;
        private SurfaceTextureBrowserViewModel? _surfaceTextureBrowser;
        private RenderSurfaceBrowserViewModel? _renderSurfaceBrowser;
        private SurfaceBrowserViewModel? _surfaceBrowser;
        private MotionTableBrowserViewModel? _motionTableBrowser;
        private WaveBrowserViewModel? _waveBrowser;
        private EnvironmentBrowserViewModel? _environmentBrowser;
        private ChatPoseTableBrowserViewModel? _chatPoseTableBrowser;
        private ObjectHierarchyBrowserViewModel? _objectHierarchyBrowser;
        private BadDataTableBrowserViewModel? _badDataTableBrowser;
        private TabooTableBrowserViewModel? _tabooTableBrowser;
        private NameFilterTableBrowserViewModel? _nameFilterTableBrowser;
        private PalSetBrowserViewModel? _palSetBrowser;
        private ClothingTableBrowserViewModel? _clothingTableBrowser;
        private GfxObjDegradeInfoBrowserViewModel? _gfxObjDegradeInfoBrowser;
        private SceneBrowserViewModel? _sceneBrowser;
        private RegionBrowserViewModel? _regionBrowser;
        private MasterInputMapBrowserViewModel? _masterInputMapBrowser;
        private RenderTextureBrowserViewModel? _renderTextureBrowser;
        private RenderMaterialBrowserViewModel? _renderMaterialBrowser;
        private MaterialModifierBrowserViewModel? _materialModifierBrowser;
        private MaterialInstanceBrowserViewModel? _materialInstanceBrowser;
        private SoundTableBrowserViewModel? _soundTableBrowser;
        private EnumMapperBrowserViewModel? _enumMapperBrowser;
        private StringTableBrowserViewModel? _stringTableBrowser;
        private EnumIDMapBrowserViewModel? _enumIDMapBrowser;
        private ActionMapBrowserViewModel? _actionMapBrowser;
        private DualEnumIDMapBrowserViewModel? _dualEnumIDMapBrowser;
        private LanguageStringBrowserViewModel? _languageStringBrowser;
        private ParticleEmitterBrowserViewModel? _particleEmitterBrowser;
        private PhysicsScriptBrowserViewModel? _physicsScriptBrowser;
        private PhysicsScriptTableBrowserViewModel? _physicsScriptTableBrowser;
        private MasterPropertyBrowserViewModel? _masterPropertyBrowser;
        private FontBrowserViewModel? _fontBrowser;
        private DBPropertiesBrowserViewModel? _dbPropertiesBrowser;
        private CharGenBrowserViewModel? _charGenBrowser;
        private VitalTableBrowserViewModel? _vitalTableBrowser;
        private SkillTableBrowserViewModel? _skillTableBrowser;
        private SpellTableBrowserViewModel? _spellTableBrowser;
        private SpellComponentTableBrowserViewModel? _spellComponentTableBrowser;
        private ExperienceTableBrowserViewModel? _experienceTableBrowser;
        private QualityFilterBrowserViewModel? _qualityFilterBrowser;
        private CombatTableBrowserViewModel? _combatTableBrowser;
        private ContractTableBrowserViewModel? _contractTableBrowser;
        private LandBlockBrowserViewModel? _landBlockBrowser;
        private LandBlockInfoBrowserViewModel? _landBlockInfoBrowser;
        private EnvCellBrowserViewModel? _envCellBrowser;
        private LayoutDescBrowserViewModel? _layoutDescBrowser;
        private LanguageInfoBrowserViewModel? _languageInfoBrowser;
        private readonly WorldBuilder.Shared.Lib.IInputManager _inputManager;
        private readonly IKeywordRepositoryService _keywordRepository;
        private readonly ProjectManager _projectManager;

        public IDatReaderWriter Dats => _dats;

        public DatBrowserViewModel(IDatBrowserViewModelFactory viewModelFactory, IDialogService dialogService, IServiceProvider serviceProvider, IDatReaderWriter dats, WorldBuilder.Shared.Lib.IInputManager inputManager, IKeywordRepositoryService keywordRepository, ProjectManager projectManager) {
            _viewModelFactory = viewModelFactory;
            _dialogService = dialogService;
            _serviceProvider = serviceProvider;
            _dats = dats;
            _inputManager = inputManager;
            _keywordRepository = keywordRepository;
            _projectManager = projectManager;

            SelectedType = DBObjType.Setup;
            // Don't create browser here - let the lazy loading handle it
            CurrentBrowser = null;
            // Trigger the lazy loading for Setup type
            OnSelectedTypeChanged(DBObjType.Setup);
        }

        [RelayCommand]
        private void Browse() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.SelectedFileId = 0;
            }
        }

        [RelayCommand]
        private void Back() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.SelectedFileId = 0;
            }
        }

        partial void OnSelectedTypeChanged(DBObjType value) {
            CurrentBrowser = value switch {
                DBObjType.Iteration => _iterationBrowser ??= _viewModelFactory.CreateIterationBrowser(),
                DBObjType.GfxObj => _gfxObjBrowser ??= _viewModelFactory.CreateGfxObjBrowser(),
                DBObjType.Setup => _setupBrowser ??= _viewModelFactory.CreateSetupBrowser(),
                DBObjType.Animation => _animationBrowser ??= _viewModelFactory.CreateAnimationBrowser(),
                DBObjType.Palette => _paletteBrowser ??= _viewModelFactory.CreatePaletteBrowser(),
                DBObjType.SurfaceTexture => _surfaceTextureBrowser ??= _viewModelFactory.CreateSurfaceTextureBrowser(),
                DBObjType.RenderSurface => _renderSurfaceBrowser ??= _viewModelFactory.CreateRenderSurfaceBrowser(),
                DBObjType.Surface => _surfaceBrowser ??= _viewModelFactory.CreateSurfaceBrowser(),
                DBObjType.MotionTable => _motionTableBrowser ??= _viewModelFactory.CreateMotionTableBrowser(),
                DBObjType.Wave => _waveBrowser ??= _viewModelFactory.CreateWaveBrowser(),
                DBObjType.Environment => _environmentBrowser ??= _viewModelFactory.CreateEnvironmentBrowser(),
                DBObjType.ChatPoseTable => _chatPoseTableBrowser ??= _viewModelFactory.CreateChatPoseTableBrowser(),
                DBObjType.ObjectHierarchy => _objectHierarchyBrowser ??= _viewModelFactory.CreateObjectHierarchyBrowser(),
                DBObjType.BadDataTable => _badDataTableBrowser ??= _viewModelFactory.CreateBadDataTableBrowser(),
                DBObjType.TabooTable => _tabooTableBrowser ??= _viewModelFactory.CreateTabooTableBrowser(),
                DBObjType.NameFilterTable => _nameFilterTableBrowser ??= _viewModelFactory.CreateNameFilterTableBrowser(),
                DBObjType.PalSet => _palSetBrowser ??= _viewModelFactory.CreatePalSetBrowser(),
                DBObjType.ClothingTable => _clothingTableBrowser ??= _viewModelFactory.CreateClothingTableBrowser(),
                DBObjType.GfxObjDegradeInfo => _gfxObjDegradeInfoBrowser ??= _viewModelFactory.CreateGfxObjDegradeInfoBrowser(),
                DBObjType.Scene => _sceneBrowser ??= _viewModelFactory.CreateSceneBrowser(),
                DBObjType.Region => _regionBrowser ??= _viewModelFactory.CreateRegionBrowser(),
                DBObjType.MasterInputMap => _masterInputMapBrowser ??= _viewModelFactory.CreateMasterInputMapBrowser(),
                DBObjType.RenderTexture => _renderTextureBrowser ??= _viewModelFactory.CreateRenderTextureBrowser(),
                DBObjType.RenderMaterial => _renderMaterialBrowser ??= _viewModelFactory.CreateRenderMaterialBrowser(),
                DBObjType.MaterialModifier => _materialModifierBrowser ??= _viewModelFactory.CreateMaterialModifierBrowser(),
                DBObjType.MaterialInstance => _materialInstanceBrowser ??= _viewModelFactory.CreateMaterialInstanceBrowser(),
                DBObjType.SoundTable => _soundTableBrowser ??= _viewModelFactory.CreateSoundTableBrowser(),
                DBObjType.EnumMapper => _enumMapperBrowser ??= _viewModelFactory.CreateEnumMapperBrowser(),
                DBObjType.StringTable => _stringTableBrowser ??= _viewModelFactory.CreateStringTableBrowser(),
                DBObjType.EnumIDMap => _enumIDMapBrowser ??= _viewModelFactory.CreateEnumIDMapBrowser(),
                DBObjType.ActionMap => _actionMapBrowser ??= _viewModelFactory.CreateActionMapBrowser(),
                DBObjType.DualEnumIDMap => _dualEnumIDMapBrowser ??= _viewModelFactory.CreateDualEnumIDMapBrowser(),
                DBObjType.LanguageString => _languageStringBrowser ??= _viewModelFactory.CreateLanguageStringBrowser(),
                DBObjType.ParticleEmitter => _particleEmitterBrowser ??= _viewModelFactory.CreateParticleEmitterBrowser(),
                DBObjType.PhysicsScript => _physicsScriptBrowser ??= _viewModelFactory.CreatePhysicsScriptBrowser(),
                DBObjType.PhysicsScriptTable => _physicsScriptTableBrowser ??= _viewModelFactory.CreatePhysicsScriptTableBrowser(),
                DBObjType.MasterProperty => _masterPropertyBrowser ??= _viewModelFactory.CreateMasterPropertyBrowser(),
                DBObjType.Font => _fontBrowser ??= _viewModelFactory.CreateFontBrowser(),
                DBObjType.DBProperties => _dbPropertiesBrowser ??= _viewModelFactory.CreateDBPropertiesBrowser(),
                DBObjType.CharGen => _charGenBrowser ??= _viewModelFactory.CreateCharGenBrowser(),
                DBObjType.VitalTable => _vitalTableBrowser ??= _viewModelFactory.CreateVitalTableBrowser(),
                DBObjType.SkillTable => _skillTableBrowser ??= _viewModelFactory.CreateSkillTableBrowser(),
                DBObjType.SpellTable => _spellTableBrowser ??= _viewModelFactory.CreateSpellTableBrowser(),
                DBObjType.SpellComponentTable => _spellComponentTableBrowser ??= _viewModelFactory.CreateSpellComponentTableBrowser(),
                DBObjType.ExperienceTable => _experienceTableBrowser ??= _viewModelFactory.CreateExperienceTableBrowser(),
                DBObjType.QualityFilter => _qualityFilterBrowser ??= _viewModelFactory.CreateQualityFilterBrowser(),
                DBObjType.CombatTable => _combatTableBrowser ??= _viewModelFactory.CreateCombatTableBrowser(),
                DBObjType.ContractTable => _contractTableBrowser ??= _viewModelFactory.CreateContractTableBrowser(),
                DBObjType.LandBlock => GetOrCreateLandBlockBrowser(),
                DBObjType.LandBlockInfo => GetOrCreateLandBlockInfoBrowser(),
                DBObjType.EnvCell => GetOrCreateEnvCellBrowser(),
                DBObjType.LayoutDesc => _layoutDescBrowser ??= _viewModelFactory.CreateLayoutDescBrowser(),
                DBObjType.LanguageInfo => _languageInfoBrowser ??= _viewModelFactory.CreateLanguageInfoBrowser(),
                _ => null
            };
        }

        private EnvCellBrowserViewModel GetOrCreateEnvCellBrowser() {
            if (_envCellBrowser == null) {
                _envCellBrowser = _viewModelFactory.CreateEnvCellBrowser();
            }
            
            // Initialize EnvCell data on first access
            if (_envCellBrowser.FileIds == null || !_envCellBrowser.FileIds.Any()) {
                _envCellBrowser.LoadEnvCellData();
            }
            
            return _envCellBrowser;
        }

        private LandBlockBrowserViewModel GetOrCreateLandBlockBrowser() {
            if (_landBlockBrowser == null) {
                _landBlockBrowser = _viewModelFactory.CreateLandBlockBrowser();
            }
            
            // Initialize LandBlock data on first access
            if (_landBlockBrowser.FileIds == null || !_landBlockBrowser.FileIds.Any()) {
                _landBlockBrowser.LoadLandBlockData();
            }
            
            return _landBlockBrowser;
        }

        private LandBlockInfoBrowserViewModel GetOrCreateLandBlockInfoBrowser() {
            if (_landBlockInfoBrowser == null) {
                _landBlockInfoBrowser = _viewModelFactory.CreateLandBlockInfoBrowser();
            }
            
            // Initialize LandBlockInfo data on first access
            if (_landBlockInfoBrowser.FileIds == null || !_landBlockInfoBrowser.FileIds.Any()) {
                _landBlockInfoBrowser.LoadLandBlockInfoData();
            }
            
            return _landBlockInfoBrowser;
        }

        partial void OnCurrentBrowserChanged(ViewModelBase? oldValue, ViewModelBase? newValue) {
            if (oldValue is INotifyPropertyChanged oldNotify) {
                oldNotify.PropertyChanged -= OnBrowserPropertyChanged;
            }
            if (oldValue is IDatBrowserViewModel oldBrowser && oldBrowser.GridBrowser != null) {
                oldBrowser.GridBrowser.PropertyChanged -= OnGridBrowserPropertyChanged;
            }
            if (newValue is INotifyPropertyChanged newNotify) {
                newNotify.PropertyChanged += OnBrowserPropertyChanged;
            }
            if (newValue is IDatBrowserViewModel newBrowser && newBrowser.GridBrowser != null) {
                newBrowser.GridBrowser.PropertyChanged += OnGridBrowserPropertyChanged;
            }
            OnPropertyChanged(nameof(GridBrowser));
            UpdateSearchProperties();
            UpdateSelectedObject();
        }

        private void OnBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (e.PropertyName == nameof(IDatBrowserViewModel.SelectedObject)) {
                UpdateSelectedObject();
            }
            if (sender is SurfaceTextureBrowserViewModel stBrowser && e.PropertyName == nameof(SurfaceTextureBrowserViewModel.PreviewFileId)) {
                if (ObjectOverview is SurfaceTextureOverviewViewModel stovm) {
                    stovm.SelectedTextureId = stBrowser.PreviewFileId;
                }
            }
            if (sender is IDatBrowserViewModel browser) {
                if (e.PropertyName == nameof(IDatBrowserViewModel.KeywordsSearchText)) {
                    KeywordsSearchText = browser.KeywordsSearchText;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.IsKeywordsSearchEnabled)) {
                    IsKeywordsSearchEnabled = browser.IsKeywordsSearchEnabled;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.ShowKeywordsSearchWarning)) {
                    ShowKeywordsSearchWarning = browser.ShowKeywordsSearchWarning;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.KeywordsSearchTooltip)) {
                    KeywordsSearchTooltip = browser.KeywordsSearchTooltip;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.IsEmbeddingSearchActive)) {
                    IsEmbeddingSearchActive = browser.IsEmbeddingSearchActive;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.IsSearchTypeSelectionEnabled)) {
                    IsSearchTypeSelectionEnabled = browser.IsSearchTypeSelectionEnabled;
                }
                else if (e.PropertyName == nameof(IDatBrowserViewModel.SearchType)) {
                    SearchType = browser.SearchType;
                }
            }
        }

        private void OnGridBrowserPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is GridBrowserViewModel gridBrowser) {
                if (e.PropertyName == nameof(GridBrowserViewModel.IsKeywordsSearching)) {
                    IsKeywordsSearching = gridBrowser.IsKeywordsSearching;
                }
                else if (e.PropertyName == nameof(GridBrowserViewModel.IsEmbeddingSearchActive)) {
                    IsEmbeddingSearchActive = gridBrowser.IsEmbeddingSearchActive;
                }
            }
        }

        private void UpdateSearchProperties() {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                KeywordsSearchText = browser.KeywordsSearchText;
                IsKeywordsSearchEnabled = browser.IsKeywordsSearchEnabled;
                ShowKeywordsSearchWarning = browser.ShowKeywordsSearchWarning;
                KeywordsSearchTooltip = browser.KeywordsSearchTooltip;
                IsEmbeddingSearchActive = browser.IsEmbeddingSearchActive;
                IsSearchTypeSelectionEnabled = browser.IsSearchTypeSelectionEnabled;
                SearchType = browser.SearchType;
                if (browser.GridBrowser != null) {
                    IsKeywordsSearching = browser.GridBrowser.IsKeywordsSearching;
                    IsEmbeddingSearchActive = browser.GridBrowser.IsEmbeddingSearchActive;
                }
            }
            else {
                KeywordsSearchText = string.Empty;
                IsKeywordsSearchEnabled = false;
                ShowKeywordsSearchWarning = false;
                KeywordsSearchTooltip = string.Empty;
                IsKeywordsSearching = false;
                IsEmbeddingSearchActive = false;
                IsSearchTypeSelectionEnabled = false;
            }
        }

        partial void OnKeywordsSearchTextChanged(string value) {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.KeywordsSearchText = value;
            }
        }

        partial void OnSearchTypeChanged(SearchType value) {
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                browser.SearchType = value;
            }
        }

        private void UpdateSelectedObject() {
            if (_isSettingObject) return;
            if (CurrentBrowser is IDatBrowserViewModel browser) {
                SelectedObject = browser.SelectedObject;
            }
            else {
                SelectedObject = null;
            }
        }

        partial void OnSelectedObjectChanged(IDBObj? value) {
            if (ObjectOverview is INotifyPropertyChanged oldNotify) {
                oldNotify.PropertyChanged -= OnOverviewPropertyChanged;
            }
            ReflectionNodes.Clear();
            ObjectOverview = CreateOverview(value);
            if (ObjectOverview is INotifyPropertyChanged newNotify) {
                newNotify.PropertyChanged += OnOverviewPropertyChanged;
            }
            SelectedPropertiesTabIndex = ObjectOverview != null ? 0 : 1;
            UpdateCurrentKeywords(value);

            if (value != null) {
                _isSettingObject = true;
                try {
                    var resolutions = Dats.ResolveId(value.Id).ToList();
                    if (resolutions.Count > 0) {
                        SelectedType = resolutions.First().Type;
                    }

                    if (ObjectOverview is SurfaceTextureOverviewViewModel stovm) {
                        PreviewFileId = stovm.SelectedTextureId;
                    }
                    else {
                        PreviewFileId = value.Id;
                    }

                    if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                        stBrowser.PreviewFileId = PreviewFileId;
                    }

                    if (CurrentBrowser is IDatBrowserViewModel browser) {
                        browser.SelectedFileId = value.Id;
                    }
                }
                finally {
                    _isSettingObject = false;
                }
            }
            else {
                PreviewFileId = 0;
                if (CurrentBrowser is IDatBrowserViewModel browser) {
                    browser.SelectedFileId = 0;
                }
            }

            if (value != null) {
                Task.Run(() => {
                    var root = ReflectionNodeViewModel.Create("Root", value, _dats);
                    var children = root.Children?.ToList() ?? new List<ReflectionNodeViewModel>();
                    Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                        foreach (var child in children) {
                            ReflectionNodes.Add(child);
                        }
                    });
                });
            }
        }

        public bool HandleHotkey(KeyEventArgs e) {
            if (_inputManager is InputManager inputManager && inputManager.IsAction(e, InputAction.GoToFileId)) {
                _ = ShowGoToFileIdPrompt();
                return true;
            }
            return false;
        }

        private async Task ShowGoToFileIdPrompt() {
            var vm = _dialogService.CreateViewModel<TextInputWindowViewModel>();
            vm.Title = "Go To File ID";
            vm.Message = "Enter File ID (hex or decimal):";

            var owner = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow?.DataContext as INotifyPropertyChanged;
            if (owner != null) {
                await _dialogService.ShowDialogAsync(owner, vm);
            }
            else {
                await _dialogService.ShowDialogAsync(null!, vm);
            }

            if (vm.Result) {
                uint fileId = 0;
                var input = vm.InputText.Trim();
                if (input.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) {
                    uint.TryParse(input.Substring(2), System.Globalization.NumberStyles.HexNumber, null, out fileId);
                }
                else {
                    uint.TryParse(input, out fileId);
                }

                if (fileId != 0) {
                    NavigateToFileId(fileId);
                }
            }
        }

        private void NavigateToFileId(uint fileId) {
            var resolutions = _dats.ResolveId(fileId).ToList();
            if (resolutions.Count > 0) {
                var res = resolutions.First();
                if (res.Database.TryGet<IDBObj>(fileId, out var obj)) {
                    SelectedObject = obj;
                }
            }
        }

        private void OnOverviewPropertyChanged(object? sender, PropertyChangedEventArgs e) {
            if (sender is SurfaceTextureOverviewViewModel stovm && (e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTextureId) || e.PropertyName == nameof(SurfaceTextureOverviewViewModel.SelectedTexture))) {
                PreviewFileId = stovm.SelectedTextureId;
                if (CurrentBrowser is SurfaceTextureBrowserViewModel stBrowser) {
                    stBrowser.PreviewFileId = stovm.SelectedTextureId;
                }
            }
            if (sender is EnvCellOverviewViewModel ecovm && e.PropertyName == nameof(EnvCellOverviewViewModel.SelectedItem)) {
                if (ecovm.SelectedItem != null && ecovm.SelectedItem.DataId.HasValue) {
                    PreviewFileId = ecovm.SelectedItem.DataId.Value;
                }
            }
        }

        private object? CreateOverview(IDBObj? obj) {
            if (obj is SurfaceTexture surfaceTexture) {
                return new SurfaceTextureOverviewViewModel(surfaceTexture, _dats);
            }
            if (obj is EnvCell envCell) {
                return new EnvCellOverviewViewModel(envCell, _dats);
            }
            if (obj is ParticleEmitter particleEmitter) {
                return new ParticleEmitterOverviewViewModel(particleEmitter, _dats);
            }
            return null;
        }

        private void UpdateCurrentKeywords(IDBObj? obj) {
            if (obj is DatReaderWriter.DBObjs.Setup setup) {
                var project = _projectManager.CurrentProject;
                if (project != null && project.ManagedIds.ManagedDatSetId.HasValue && project.ManagedIds.ManagedAceDbId.HasValue) {
                    Task.Run(async () => {
                        var keywords = await _keywordRepository.GetKeywordsForSetupAsync(project.ManagedIds.ManagedDatSetId.Value, project.ManagedIds.ManagedAceDbId.Value, setup.Id, default);
                        Avalonia.Threading.Dispatcher.UIThread.Post(() => {
                            _showKeywords = keywords.HasValue;
                            OnPropertyChanged(nameof(ShowKeywords));
                            CurrentKeywordsNames = keywords?.Names;
                            CurrentKeywordsTags = keywords?.Tags;
                            CurrentKeywordsDescriptions = keywords?.Descriptions;
                        });
                    });
                    return;
                }
            }
            _showKeywords = false;
            OnPropertyChanged(nameof(ShowKeywords));
            CurrentKeywordsNames = null;
            CurrentKeywordsTags = null;
            CurrentKeywordsDescriptions = null;
        }
    }
}
