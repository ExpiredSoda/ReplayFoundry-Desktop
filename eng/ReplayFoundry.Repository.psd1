@{
    SchemaVersion = 1

    RepositorySolution = 'ReplayFoundry.slnx'

    ProductProjects = @(
        'src/ReplayFoundry.Desktop/ReplayFoundry.Desktop.csproj'
        'src/ReplayFoundry.RuntimePacks/ReplayFoundry.RuntimePacks.csproj'
    )

    ToolProjects = @(
        'tools/ReplayFoundry.RuntimeInstaller/ReplayFoundry.RuntimeInstaller.csproj'
        'tools/ReplayFoundry.DeveloperTools/ReplayFoundry.DeveloperTools.csproj'
    )

    TestSupportProjects = @(
        'tests/ReplayFoundry.Testing/ReplayFoundry.Testing.csproj'
    )

    DotNetTestProjects = @(
        'tests/ReplayFoundry.CompositionTests/ReplayFoundry.CompositionTests.csproj'
        'tests/ReplayFoundry.DeveloperTools.Tests/ReplayFoundry.DeveloperTools.Tests.csproj'
        'tests/ReplayFoundry.EvidenceTests/ReplayFoundry.EvidenceTests.csproj'
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
        'eng/Test-MediaEvidenceArchitecture.ps1'
        'eng/Test-MomentFinderArchitecture.ps1'
        'eng/Test-ReleaseEngineering.ps1'
        'eng/Test-ReleaseDataBoundaryArchitecture.ps1'
        'eng/Test-RepositoryArchitecture.ps1'
        'eng/Test-RepositoryPayloadGuard.ps1'
        'eng/Test-ReleaseDataBoundary.ps1'
        'eng/Test-RuntimePackArchitecture.ps1'
        'eng/Test-SecurityBoundaries.ps1'
        'eng/Test-UiUxArchitecture.ps1'
        'eng/Test-UiUxHumanCenteredExperience.ps1'
        'eng/Test-UiUxVisualSystem.ps1'
        'eng/Test-VisualSemanticArchitecture.ps1'
        'eng/Test-VisualSemanticPrompt2Architecture.ps1'
        'eng/Test-VisualSemanticStructuredDecodingArchitecture.ps1'
    )

    PublicExport = @{
        RootFiles = @(
            '.editorconfig'
            '.gitattributes'
            '.gitignore'
            'Directory.Build.props'
            'LICENSE.txt'
            'README.md'
            'CHANGELOG.md'
            'SECURITY.md'
        )

        ExactFiles = @(
            '.github/assets/buy-me-a-coffee-qr.png'
            '.github/assets/replayfoundry-demo-poster.jpg'
            '.github/assets/replayfoundry-workflow-hero.gif'
            '.github/assets/setup-base-advanced.png'
            '.github/assets/workflow-01-generate.gif'
            '.github/assets/workflow-02-studio.gif'
            '.github/assets/workflow-03-library.gif'
            '.github/assets/workflow-04-publish.gif'
            '.github/workflows/desktop-ci.yml'
            '.github/workflows/redistributable-ffmpeg.yml'
            'docs/README.md'
            'eng/Assert-ReplayFoundryRuntimePackCatalogBinding.ps1'
            'eng/Build-ReplayFoundryInstaller.ps1'
            'eng/Build-ReplayFoundryRuntimePacks.ps1'
            'eng/Copy-VerifiedAudioEvidenceModel.ps1'
            'eng/Prepare-AudioEvidenceModel.py'
            'tools/evaluate_moment_discernment.py'
            'eng/Copy-ReplayFoundryProductionVisualHost.ps1'
            'eng/Invoke-ReplayFoundryArtifactSigning.ps1'
            'eng/New-PythonRuntimeNotices.ps1'
            'eng/New-AccelerateSecurityWheel.py'
            'eng/New-ReplayFoundryBrandAssets.ps1'
            'eng/New-ReplayFoundryInstallerBranding.ps1'
            'eng/New-ReplayFoundryRuntimePackCatalog.ps1'
            'eng/Publish-ReplayFoundryWindows.ps1'
            'eng/Resolve-ReplayFoundryWinSparkle.ps1'
            'eng/ReplayFoundry.Updates.psd1'
            'eng/Initialize-ReplayFoundryUpdateSigning.ps1'
            'eng/New-ReplayFoundryUpdateAppcast.ps1'
            'eng/ReplayFoundry.ps1'
            'eng/ReplayFoundry.ProductionVisualHost.psd1'
            'eng/ReplayFoundry.Repository.psd1'
            'eng/Resolve-ReplayFoundryArtifactSigningClient.ps1'
            'eng/Test-CreativeCommerceArchitecture.ps1'
            'eng/Test-AiRuntimeDependencies.ps1'
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

        RequiredFiles = @(
            'CHANGELOG.md'
            '.github/assets/buy-me-a-coffee-qr.png'
            '.github/assets/replayfoundry-demo-poster.jpg'
            '.github/assets/replayfoundry-workflow-hero.gif'
            '.github/assets/setup-base-advanced.png'
            '.github/assets/workflow-01-generate.gif'
            '.github/assets/workflow-02-studio.gif'
            '.github/assets/workflow-03-library.gif'
            '.github/assets/workflow-04-publish.gif'
            '.github/workflows/desktop-ci.yml'
            '.github/workflows/redistributable-ffmpeg.yml'
            'docs/README.md'
            'eng/ReplayFoundry.ps1'
            'eng/ReplayFoundry.Repository.psd1'
            'eng/ReplayFoundry.ProductionVisualHost.psd1'
            'eng/Assert-ReplayFoundryRuntimePackCatalogBinding.ps1'
            'eng/Copy-ReplayFoundryProductionVisualHost.ps1'
            'eng/Test-RepositoryArchitecture.ps1'
            'eng/Test-ReleaseDataBoundary.ps1'
        )

        PublicSourceReplacements = @(
            @{
                Template = 'eng/PublicSourceTemplates/foundry_writer_runtime.py'
                Destination = 'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/runtime.py'
            }
            @{
                Template = 'eng/PublicSourceTemplates/TasteLearningService.cs'
                Destination = 'src/ReplayFoundry.Desktop/Platform/Intelligence/TasteLearningService.cs'
            }
            @{
                Template = 'eng/PublicSourceTemplates/Directory.Build.props'
                Destination = 'Directory.Build.props'
            }
            @{
                Template = 'eng/PublicSourceTemplates/ReplayFoundry.Repository.psd1'
                Destination = 'eng/ReplayFoundry.Repository.psd1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/ReplayFoundry.ProductionVisualHost.psd1'
                Destination = 'eng/ReplayFoundry.ProductionVisualHost.psd1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/ReplayFoundry.ps1'
                Destination = 'eng/ReplayFoundry.ps1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/Test-RepositoryArchitecture.ps1'
                Destination = 'eng/Test-RepositoryArchitecture.ps1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/Test-ReleaseDataBoundary.ps1'
                Destination = 'eng/Test-ReleaseDataBoundary.ps1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/Test-RepositoryPayloadGuard.ps1'
                Destination = 'eng/Test-RepositoryPayloadGuard.ps1'
            }
            @{
                Template = 'eng/PublicSourceTemplates/desktop-ci.yml'
                Destination = '.github/workflows/desktop-ci.yml'
            }
            @{
                Template = 'eng/PublicSourceTemplates/AssemblyInfo.cs'
                Destination = 'src/ReplayFoundry.Desktop/AssemblyInfo.cs'
            }
        )

        PrivateRoots = @('src/ReplayFoundry.Desktop/Media/Intelligence/Learning', 'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer')
        PublicContracts = @(
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteContracts.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteMomentCorrection.cs'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/__init__.py'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/capture.py'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/data.py'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/editorial/writer/runtime.py'
        )

        Roots = @(
            'src/ReplayFoundry.Desktop'
            'src/ReplayFoundry.RuntimePacks'
            'tools/ReplayFoundry.RuntimeInstaller'
            'tests/ReplayFoundry.CompositionTests'
            'tests/ReplayFoundry.InspectionTests'
            'tests/ReplayFoundry.PreparationTests'
            'tests/ReplayFoundry.RuntimePacks.Tests'
            'tests/ReplayFoundry.Testing'
            'docs/distribution'
            'installer'
        )

        ExcludedFiles = @(
            'tests/ReplayFoundry.VisualSemanticHost.Tests/tests/test_writer_learning_loop.py'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/curation.py'
            'src/ReplayFoundry.VisualSemanticHost/replayfoundry_visual_semantic/curation_train.py'
            'tests/ReplayFoundry.VisualSemanticHost.Tests/tests/test_curation.py'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteDifferentiation.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteEvaluation.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteNetwork.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteOptimizer.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteTrainer.cs'
            'src/ReplayFoundry.Desktop/Media/Intelligence/Learning/TasteLinearBaseline.cs'
            'src/ReplayFoundry.Desktop/Platform/Storage/JsonTasteLearningStore.cs'
            'tests/ReplayFoundry.PreparationTests/TasteLearningTests.cs'
            'tests/ReplayFoundry.PreparationTests/TasteStorageTests.cs'
            'tests/ReplayFoundry.PreparationTests/MomentCorrectionTests.cs'
            'eng/Copy-ReplayFoundryDevelopmentState.ps1'
            'eng/Export-ReplayFoundryProductionRepository.ps1'
            'eng/Test-ReleaseDataBoundaryArchitecture.ps1'
            'eng/Test-MediaEvidenceArchitecture.ps1'
            'eng/Test-VisualSemanticArchitecture.ps1'
            'eng/Test-VisualSemanticPrompt2Architecture.ps1'
            'eng/Test-VisualSemanticStructuredDecodingArchitecture.ps1'
        )

        ForbiddenRoots = @(
            'ReplayFoundry.Desktop'
            'ReplayFoundry.DeveloperTools'
            'ReplayFoundry.DeveloperTools.Tests'
            'ReplayFoundry.EvidenceTests'
            'tools/ReplayFoundry.DeveloperTools'
            'tests/ReplayFoundry.DeveloperTools.Tests'
            'tests/ReplayFoundry.EvidenceTests'
            'tmp'
            'outputs'
            'artifacts'
            'installer/Output'
        )

        ForbiddenExtensions = @(
            '.exe'
            '.dll'
            '.pdb'
            '.zip'
            '.7z'
            '.onnx'
            '.bin'
            '.safetensors'
            '.pfx'
            '.p12'
            '.cer'
            '.key'
            '.pem'
            '.dpapi'
            '.mp4'
            '.mkv'
            '.mov'
            '.wav'
        )
    }
}
