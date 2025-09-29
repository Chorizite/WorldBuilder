using WorldBuilder.ViewModels;

namespace WorldBuilder.Lib.Messages {
    public class CreateProjectMessage {
        public CreateProjectViewModel CreateProjectViewModel;

        public CreateProjectMessage(CreateProjectViewModel createProjectViewModel) {
            this.CreateProjectViewModel = createProjectViewModel;
        }
    }
}