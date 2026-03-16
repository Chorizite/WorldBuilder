using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using WorldBuilder.Lib;
using WorldBuilder.Services;
using WorldBuilder.Shared.Services;
using WorldBuilder.ViewModels;
using Xunit;
using HanumanInstitute.MvvmDialogs;
using HanumanInstitute.MvvmDialogs.FrameworkDialogs;
using WorldBuilder.Messages;
using CommunityToolkit.Mvvm.Messaging;

public class ManageDatsViewModelTests {
    private readonly Mock<IDatRepositoryService> _mockDatRepo;
    private readonly Mock<IAceRepositoryService> _mockAceRepo;
    private readonly Mock<IKeywordRepositoryService> _mockKeywordRepo;
    private readonly Mock<IDialogService> _mockDialog;
    private readonly WorldBuilderSettings _settings;
    private readonly ManageDatsViewModel _viewModel;

    public ManageDatsViewModelTests() {
        _mockDatRepo = new Mock<IDatRepositoryService>();
        _mockAceRepo = new Mock<IAceRepositoryService>();
        _mockKeywordRepo = new Mock<IKeywordRepositoryService>();
        _mockDialog = new Mock<IDialogService>();
        _settings = new WorldBuilderSettings();

        _mockDatRepo.Setup(r => r.GetManagedDataSets()).Returns(new List<ManagedDatSet>());
        _mockAceRepo.Setup(r => r.GetManagedAceDbs()).Returns(new List<ManagedAceDb>());
        _mockKeywordRepo.Setup(r => r.GetManagedKeywordDbs()).Returns(new List<ManagedKeywordDb>());

        _viewModel = new ManageDatsViewModel(_settings, new NullLogger<ManageDatsViewModel>(), _mockDatRepo.Object, _mockAceRepo.Object, _mockKeywordRepo.Object, _mockDialog.Object);
    }

    [Fact]
    public void Constructor_LoadsManagedDataSets() {
        // Arrange
        var sets = new List<ManagedDatSet> {
            new ManagedDatSet { Id = Guid.NewGuid(), FriendlyName = "Set 1" },
            new ManagedDatSet { Id = Guid.NewGuid(), FriendlyName = "Set 2" }
        };
        _mockDatRepo.Setup(r => r.GetManagedDataSets()).Returns(sets);
        _mockAceRepo.Setup(r => r.GetManagedAceDbs()).Returns(new List<ManagedAceDb>());
        _mockKeywordRepo.Setup(r => r.GetManagedKeywordDbs()).Returns(new List<ManagedKeywordDb>());

        // Act - Need a new VM instance to trigger RefreshList in constructor
        var vm = new ManageDatsViewModel(_settings, new NullLogger<ManageDatsViewModel>(), _mockDatRepo.Object, _mockAceRepo.Object, _mockKeywordRepo.Object, _mockDialog.Object);
        // Assert
        Assert.Equal(2, vm.ManagedDataSets.Count);
        Assert.Equal("Set 1", vm.ManagedDataSets[0].FriendlyName);
        Assert.Equal("Set 2", vm.ManagedDataSets[1].FriendlyName);
    }

    [Fact]
    public void Constructor_LoadsManagedKeywordDbs() {
        // Arrange
        var datId = Guid.NewGuid();
        var aceId = Guid.NewGuid();
        var keywords = new List<ManagedKeywordDb> {
            new ManagedKeywordDb { DatSetId = datId, AceDbId = aceId, GeneratorVersion = 1, LastGenerated = DateTime.UtcNow }
        };
        _mockDatRepo.Setup(r => r.GetManagedDataSets()).Returns(new List<ManagedDatSet> { new ManagedDatSet { Id = datId, FriendlyName = "My Dat" } });
        _mockAceRepo.Setup(r => r.GetManagedAceDbs()).Returns(new List<ManagedAceDb> { new ManagedAceDb { Id = aceId, FriendlyName = "My Ace" } });
        _mockKeywordRepo.Setup(r => r.GetManagedKeywordDbs()).Returns(keywords);

        // Act
        var vm = new ManageDatsViewModel(_settings, new NullLogger<ManageDatsViewModel>(), _mockDatRepo.Object, _mockAceRepo.Object, _mockKeywordRepo.Object, _mockDialog.Object);

        // Assert
        Assert.Single(vm.ManagedKeywordDbs);
        Assert.Equal("My Dat", vm.ManagedKeywordDbs[0].DatSetName);
        Assert.Equal("My Ace", vm.ManagedKeywordDbs[0].AceDbName);
    }

    [Fact]
    public void GoBackCommand_SendsSplashPageChangedMessage() {
        // Arrange
        SplashPageChangedMessage? receivedMessage = null;
        WeakReferenceMessenger.Default.Register<SplashPageChangedMessage>(this, (r, m) => receivedMessage = m);

        try {
            // Act
            _viewModel.GoBackCommand.Execute(null);

            // Assert
            Assert.NotNull(receivedMessage);
            Assert.Equal(SplashPageViewModel.SplashPage.ProjectSelection, receivedMessage.Value);
        }
        finally {
            WeakReferenceMessenger.Default.UnregisterAll(this);
        }
    }
}
