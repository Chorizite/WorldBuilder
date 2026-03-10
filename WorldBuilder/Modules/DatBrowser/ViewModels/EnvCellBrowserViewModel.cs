using DatReaderWriter.Enums;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;

namespace WorldBuilder.Modules.DatBrowser.ViewModels {
    public partial class EnvCellBrowserViewModel : BaseDatBrowserViewModel<DatReaderWriter.DBObjs.EnvCell> {
        public EnvCellBrowserViewModel(IDatReaderWriter dats, WorldBuilderSettings settings, ThemeService themeService) : base(DBObjType.EnvCell, dats, settings, themeService, dats.CellRegions.Values.FirstOrDefault(), null, true) {
        }

        public void LoadEnvCellData() {
            base.Initialize(LoadAllEnvCellIds(_dats));
        }

        private static IEnumerable<uint> LoadAllEnvCellIds(IDatReaderWriter dats) {
            var ids = new List<uint>();
            foreach (var cellDb in dats.CellRegions.Values) {
                ids.AddRange(cellDb.Db.Tree.Select(f => f.Id).Where(id => cellDb.Db.TypeFromId(id) == DBObjType.EnvCell));
            }
            return ids.OrderBy(x => x).ToList();
        }
    }
}
