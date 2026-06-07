using Xunit;

namespace AIHelperNET.UITests;

[CollectionDefinition("UITests")]
public sealed class UITestCollection : ICollectionFixture<AppFixture> { }
