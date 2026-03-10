using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class LandBlockInfoBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.LandBlockInfo> {
        public LandBlockInfoBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.LandBlockInfo, dats, settings, themeService, dats.CellRegions.Values.FirstOrDefault(), null, true) {
        }

        public void LoadLandBlockInfoData() {
            base.Initialize(LoadAllLandBlockInfoIds(_dats));
        }

        private static IEnumerable<uint> LoadAllLandBlockInfoIds(IDatReaderWriter dats) {
            var ids = new List<uint>();
            foreach (var cellDb in dats.CellRegions.Values) {
                ids.AddRange(cellDb.Db.Tree.Select(f => f.Id).Where(id => cellDb.Db.TypeFromId(id) == DBObjType.LandBlockInfo));
            }
            return ids.OrderBy(x => x).ToList();
        }
    }
}
