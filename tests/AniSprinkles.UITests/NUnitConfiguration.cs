// One app, one emulator, one set of settings: every journey drives the same running app and changes
// state the next one reads, so nothing here runs in parallel. The unit tests' xunit-style lifecycle
// (#175) is kept so a fixture's constructor is per-test setup there and here alike.
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[assembly: NonParallelizable]
