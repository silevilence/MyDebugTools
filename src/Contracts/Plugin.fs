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

type DllTabView = {
    Content: Control
    OpenFile: string -> unit
    Close: unit -> unit
}

type DllTab = {
    PageId: string
    PointId: string
    MinVersion: int
    MaxVersion: int
    Title: string
    Create: unit -> DllTabView
}

type DllTabPoint = {
    PageId: string
    PointId: string
    Version: int
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
    abstract DllTab: DllTab -> unit
    abstract DllTabPoint: DllTabPoint -> unit
    abstract MenuPoint: MenuPoint -> unit
    abstract MenuAction: MenuExtension -> unit
    abstract Monitor: MonitorSession -> unit
    abstract Own: IDisposable -> unit

/// Optional addition to the original registration contract.
type IPageDocumentation =
    abstract Describe: pageId: string * description: string -> unit

module PageDocumentation =
    let describe (registry: Registration) pageId description =
        match registry with
        | :? IPageDocumentation as docs -> docs.Describe(pageId, description)
        | _ -> ()

type IDesktopPlugin =
    abstract Activate: Registration -> unit
