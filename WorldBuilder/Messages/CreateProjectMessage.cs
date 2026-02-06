using WorldBuilder.ViewModels;

namespace WorldBuilder.Messages {
    /// <summary>
    /// A message indicating that a project should be created.
    /// </summary>
    public class CreateProjectMessage {
        /// <summary>
        /// Gets the view model containing the project creation parameters.
        /// </summary>
        public CreateProjectViewModel CreateProjectViewModel;

        /// <summary>
        /// Initializes a new instance of the CreateProjectMessage class with the specified view model.
        /// </summary>
        /// <param name="createProjectViewModel">The view model containing the project creation parameters</param>
        public CreateProjectMessage(CreateProjectViewModel createProjectViewModel) {
            this.CreateProjectViewModel = createProjectViewModel;
        }
    }
}