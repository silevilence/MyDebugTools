namespace MyDebugTools.Contracts

open System
open Avalonia.Controls

type TargetArchitecture = X86 | X64

type TargetSource = DllFile of string | ProcessId of int

type DebugTarget = {
    Source: TargetSource
    Architecture: TargetArchitecture
    DotNetPath: string
}

type Page = {
    Id: string
    Title: string
    Create: unit -> Control
}

/// A menu action receives the provider's current selection at invocation time.
type MenuPoint = {
    PageId: string
    PointId: string
    Version: int
    Selection: unit -> string
}

type MenuExtension = {
    PageId: string
    PointId: string
    MinVersion: int
    MaxVersion: int
    Title: string
    Invoke: string -> string
}

type MonitorSession = {
    Target: unit -> string option
    Stop: unit -> System.Threading.Tasks.Task
}

/// Resources owned by an activation are released together when it stops.
type Registration =
    abstract CurrentTarget: unit -> DebugTarget option
    abstract Page: Page -> unit
    abstract MenuPoint: MenuPoint -> unit
    abstract MenuAction: MenuExtension -> unit
    abstract Monitor: MonitorSession -> unit
    abstract Own: IDisposable -> unit

type IDesktopPlugin =
    abstract Activate: Registration -> unit
