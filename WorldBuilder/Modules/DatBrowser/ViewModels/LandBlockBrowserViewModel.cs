using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class LandBlockBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.LandBlock> {
        public LandBlockBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.LandBlock, dats, settings, themeService, dats.CellRegions.Values.FirstOrDefault(), null, true) {
        }

        public void LoadLandBlockData() {
            base.Initialize(LoadAllLandBlockIds(_dats));
        }

        private static IEnumerable<uint> LoadAllLandBlockIds(IDatReaderWriter dats) {
            var ids = new List<uint>();
            foreach (var cellDb in dats.CellRegions.Values) {
                ids.AddRange(cellDb.Db.Tree.Select(f => f.Id).Where(id => cellDb.Db.TypeFromId(id) == DBObjType.LandBlock));
            }
            return ids.OrderBy(x => x).ToList();
        }
    }
}
