// Copyright (c) Microsoft Corporation.  All rights reserved.

using Xunit;

// The providers keep process-wide state (DbProviderFactories registrations, configuration defaults, static
// command counters and the metadata workspace memoizer), so test classes must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
