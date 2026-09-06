@{
    SchemaVersion = 1

    RepositorySolution = 'ReplayFoundry.slnx'

    ProductProjects = @(
        'src/ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj'
        'src/ReplayFoundry.RuntimePacks/ReplayFoundry.RuntimePacks.csproj'
    )

    ToolProjects = @(
        'tools/ReplayFoundry.RuntimeInstaller/ReplayFoundry.RuntimeInstaller.csproj'
    )

    TestSupportProjects = @(
        'tests/ReplayFoundry.Testing/ReplayFoundry.Testing.csproj'
    )

    DotNetTestProjects = @(
        'tests/ReplayFoundry.CompositionTests/ReplayFoundry.CompositionTests.csproj'
        'tests/ReplayFoundry.InspectionTests/ReplayFoundry.InspectionTests.csproj'
        'tests/ReplayFoundry.PreparationTests/ReplayFoundry.PreparationTests.csproj'
        'tests/ReplayFoundry.RuntimePacks.Tests/ReplayFoundry.RuntimePacks.Tests.csproj'
    )

    PythonTestRoot = 'tests/ReplayFoundry.VisualSemanticHost.Tests'

    ProductionVisualHostManifest =
        'eng/ReplayFoundry.ProductionVisualHost.psd1'

    ArchitectureGuards = @(
        'eng/Test-CreativeCommerceArchitecture.ps1'
        'eng/Test-GenerateWorkflowArchitecture.ps1'
        'eng/Test-InstallerBranding.ps1'
        'eng/Test-MomentFinderArchitecture.ps1'
        'eng/Test-ReleaseDataBoundary.ps1'
        'eng/Test-ReleaseEngineering.ps1'
        'eng/Test-RepositoryArchitecture.ps1'
        'eng/Test-RepositoryPayloadGuard.ps1'
        'eng/Test-SecurityBoundaries.ps1'
        'eng/Test-UiUxArchitecture.ps1'
        'eng/Test-UiUxHumanCenteredExperience.ps1'
        'eng/Test-UiUxVisualSystem.ps1'
    )

    PublicSource = @{
        RequiredFiles = @(
            '.replayfoundry-public-source'
            'CHANGELOG.md'
            'Directory.Build.props'
            'README.md'
            'ReplayFoundry.slnx'
            '.github/workflows/desktop-ci.yml'
            '.github/workflows/redistributable-ffmpeg.yml'
            'docs/README.md'
            'eng/ReplayFoundry.ps1'
            'eng/ReplayFoundry.Repository.psd1'
            'eng/ReplayFoundry.ProductionVisualHost.psd1'
            'eng/Test-RepositoryArchitecture.ps1'
            'eng/Test-ReleaseDataBoundary.ps1'
        )

        ProjectRoots = @(
            'src/ReplayFoundry.Desktop'
            'src/ReplayFoundry.RuntimePacks'
            'tools/ReplayFoundry.RuntimeInstaller'
            'tests/ReplayFoundry.CompositionTests'
            'tests/ReplayFoundry.InspectionTests'
            'tests/ReplayFoundry.PreparationTests'
            'tests/ReplayFoundry.RuntimePacks.Tests'
            'tests/ReplayFoundry.Testing'
        )

        EngineeringFiles = @(
            'eng/Assert-ReplayFoundryRuntimePackCatalogBinding.ps1'
            'eng/Build-ReplayFoundryInstaller.ps1'
            'eng/Build-ReplayFoundryRuntimePacks.ps1'
            'eng/Copy-ReplayFoundryProductionVisualHost.ps1'
            'eng/Invoke-ReplayFoundryArtifactSigning.ps1'
            'eng/New-PythonRuntimeNotices.ps1'
            'eng/New-ReplayFoundryBrandAssets.ps1'
            'eng/New-ReplayFoundryInstallerBranding.ps1'
            'eng/New-ReplayFoundryRuntimePackCatalog.ps1'
            'eng/Publish-ReplayFoundryWindows.ps1'
            'eng/ReplayFoundry.ps1'
            'eng/ReplayFoundry.ProductionVisualHost.psd1'
            'eng/ReplayFoundry.Repository.psd1'
            'eng/Resolve-ReplayFoundryArtifactSigningClient.ps1'
            'eng/Test-CreativeCommerceArchitecture.ps1'
            'eng/Test-GenerateWorkflowArchitecture.ps1'
            'eng/Test-InstallerBranding.ps1'
            'eng/Test-MomentFinderArchitecture.ps1'
            'eng/Test-ReleaseDataBoundary.ps1'
            'eng/Test-ReleaseEngineering.ps1'
            'eng/Test-RepositoryArchitecture.ps1'
            'eng/Test-RepositoryPayloadGuard.ps1'
            'eng/Test-SecurityBoundaries.ps1'
            'eng/Test-UiUxArchitecture.ps1'
            'eng/Test-UiUxHumanCenteredExperience.ps1'
            'eng/Test-UiUxVisualSystem.ps1'
        )

        WorkflowFiles = @(
            '.github/workflows/desktop-ci.yml'
            '.github/workflows/redistributable-ffmpeg.yml'
        )

        PublicationDocs = @(
            'docs/README.md'
            'docs/distribution/windows.md'
            'docs/distribution/third-party-compliance.md'
        )
    }
}
