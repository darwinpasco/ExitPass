using Xunit;
using Xunit.Sdk;

[assembly: TestFramework(
    "ExitPass.CentralPms.IntegrationTests.Shared.CentralPmsIntegrationTestFramework",
    "ExitPass.CentralPms.ContractTests")]
[assembly: CollectionBehavior(DisableTestParallelization = true)]
