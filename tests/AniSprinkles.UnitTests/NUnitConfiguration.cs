// Assembly-wide NUnit settings that keep the execution model the suite was written for under xunit (#175).
//
// FixtureLifeCycle(InstancePerTestCase): xunit built a fresh instance of the test class for every test,
// so constructors and instance fields act as per-test setup throughout this suite, and IDisposable test
// classes clean up after every test. NUnit's default reuses one instance for the whole fixture, which
// would leak state between tests.
//
// Parallelizable(Fixtures): xunit ran test classes in parallel and the tests inside a class one at a
// time. NUnit's default is to run nothing in parallel. Fixtures that touch process-wide state opt out
// with [NonParallelizable] (see AppSettings in AGENTS.md).
[assembly: FixtureLifeCycle(LifeCycle.InstancePerTestCase)]
[assembly: Parallelizable(ParallelScope.Fixtures)]
