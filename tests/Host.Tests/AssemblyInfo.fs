namespace MyDebugTools.Tests

open Xunit

// Integration tests recompile the same sample projects with different properties.
[<assembly: CollectionBehavior(DisableTestParallelization = true)>]
do ()
