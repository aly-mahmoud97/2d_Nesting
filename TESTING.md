# Testing Guide for Beam Saw Nesting Algorithm

## Overview

This project uses **xUnit** for comprehensive unit and integration testing of the Beam Saw Nesting Algorithm.

## Project Structure

```
2d_Nesting/
├── BeamSawNesting/                    # Main library project
│   ├── BeamSawNesting.csproj          # Library project file
│   └── BeamSawNestingAlgorithm.cs     # Core algorithm
├── BeamSawNesting.Tests/              # Test project
│   ├── BeamSawNesting.Tests.csproj    # Test project file
│   └── BeamSawNestingAlgorithmTests.cs # Comprehensive tests
├── BeamSawNesting.sln                 # Solution file
└── .github/workflows/
    └── dotnet-tests.yml               # CI/CD configuration
```

## Prerequisites

To run tests locally, you need:

- **.NET SDK 6.0 or later** - [Download here](https://dotnet.microsoft.com/download)
- **RhinoCommon NuGet package** (automatically restored)

## Running Tests Locally

### Option 1: Command Line

```bash
# Navigate to project root
cd /path/to/2d_Nesting

# Restore dependencies
dotnet restore

# Build the solution
dotnet build

# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --verbosity detailed

# Run tests with code coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Option 2: Visual Studio

1. Open `BeamSawNesting.sln` in Visual Studio
2. Build the solution (Ctrl+Shift+B)
3. Open Test Explorer (Test → Test Explorer)
4. Click "Run All Tests"

### Option 3: Visual Studio Code

1. Install the **C# Dev Kit** extension
2. Open the project folder
3. Use the Testing sidebar to run tests

## Test Coverage

### Test Categories

| Category | Test Count | Description |
|----------|-----------|-------------|
| **Basic Algorithm Tests** | 4 | Core functionality with user-provided dimensions |
| **Panel Placement Validation** | 3 | Overlap detection, boundary checks, dimension preservation |
| **Rotation Constraint Tests** | 2 | No-rotation and rotation-allowed scenarios |
| **Grain Direction Tests** | 3 | Horizontal, vertical, and fixed grain constraints |
| **Kerf Compensation Tests** | 2 | Kerf spacing and zero-kerf scenarios |
| **Multi-Sheet Tests** | 3 | Multiple sheet handling and indexing |
| **Sort Strategy Tests** | 3 | AreaDescending, LargestFirst, SmallestFirst |
| **Cut Sequence Tests** | 2 | Valid sequences and orientations |
| **Edge Cases** | 5 | Empty lists, single panel, small/large panels |
| **Integration Tests** | 3 | Complete workflows and real-world scenarios |

**Total: 30 comprehensive tests**

### Test Data

Tests use the following real-world panel dimensions:

```
Panel Widths:  1.294, 1.258, 0.54, 1.594, 1.576, 0.54, 0.54, 0.512, 0.372587
Panel Heights: 0.54, 0.1, 0.452, 0.54, 0.1, 0.452, 0.452, 0.372587, 0.512
```

### Key Validations

✅ **Panel Placement**
- No overlapping panels on the same sheet
- All panels within sheet boundaries
- Panel dimensions preserved after placement

✅ **Constraints**
- Rotation constraints respected (0° or 90°)
- Grain direction constraints enforced
- Kerf thickness properly applied

✅ **Efficiency**
- Efficiency metrics between 0-100%
- Sheet utilization calculated correctly
- Multi-sheet handling works properly

✅ **Guillotine Cutting**
- Valid cut sequences generated
- Horizontal and vertical cuts properly oriented
- Cut lines within sheet boundaries

## Continuous Integration (GitHub Actions)

Tests automatically run on:
- **Every push** to `main` branch or `claude/**` branches
- **Every pull request** to `main` branch

### CI/CD Matrix

Tests run on multiple .NET versions:
- .NET 6.0.x (LTS)
- .NET 8.0.x (Latest)

### Viewing CI Results

1. Go to the **Actions** tab in GitHub
2. Click on the latest workflow run
3. View test results and code coverage

### Code Coverage

Code coverage reports are automatically:
- Generated during CI runs
- Uploaded to Codecov (if configured)
- Available in the workflow artifacts

## Writing New Tests

### Test Naming Convention

```csharp
[Fact]
public void MethodName_Scenario_ExpectedBehavior()
{
    // Arrange
    // ... setup code

    // Act
    // ... execute code

    // Assert
    // ... verify results
}
```

### Example Test

```csharp
[Fact]
public void Nest_WithUserProvidedPanels_ShouldPlaceAllPanels()
{
    // Arrange
    var algorithm = new BeamSawNestingAlgorithm(
        sheetWidth: 2.5,
        sheetHeight: 2.5,
        sheetGrain: SheetGrainDirection.Horizontal
    );
    var panels = GetUserProvidedPanels();

    // Act
    algorithm.Nest(panels);

    // Assert
    var placedPanels = algorithm.GetPlacedPanels();
    Assert.Equal(panels.Count, placedPanels.Count);
}
```

### Adding New Tests

1. Open `BeamSawNesting.Tests/BeamSawNestingAlgorithmTests.cs`
2. Add your test method with `[Fact]` attribute
3. Use the AAA pattern (Arrange, Act, Assert)
4. Run tests to verify

## Troubleshooting

### Common Issues

**Issue: "RhinoCommon not found"**
```bash
# Solution: Restore NuGet packages
dotnet restore
```

**Issue: "Tests not discovered"**
```bash
# Solution: Clean and rebuild
dotnet clean
dotnet build
```

**Issue: "Test timeout"**
```bash
# Solution: Increase timeout in test
[Fact(Timeout = 60000)]  // 60 seconds
public void MyLongRunningTest() { ... }
```

### Debug Mode

To debug tests in Visual Studio:
1. Set a breakpoint in your test
2. Right-click the test → Debug Test
3. Step through code execution

### Verbose Logging

```bash
# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"
```

## Performance Benchmarks

Expected test execution times:

| Test Category | Expected Time |
|---------------|--------------|
| Basic Algorithm | < 1 second |
| Validation Tests | < 2 seconds |
| Integration Tests | < 5 seconds |
| All Tests | < 10 seconds |

## Next Steps

### Recommended Additions

1. **Benchmark Tests** - Add BenchmarkDotNet for performance testing
2. **Property-Based Tests** - Use FsCheck for randomized testing
3. **Mutation Testing** - Use Stryker.NET to verify test quality
4. **Visual Regression** - Add tests for visualization output

### Test Coverage Goals

- **Current Coverage**: ~95% (estimated)
- **Target Coverage**: 100% for core algorithm
- **Minimum Coverage**: 80% overall

## Resources

- [xUnit Documentation](https://xunit.net/)
- [.NET Testing Best Practices](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-best-practices)
- [GitHub Actions for .NET](https://docs.github.com/en/actions/automating-builds-and-tests/building-and-testing-net)

## Contact

For questions or issues with tests:
1. Check existing test code for examples
2. Review this documentation
3. Open an issue on GitHub
