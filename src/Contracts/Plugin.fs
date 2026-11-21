namespace MyDebugTools.Contracts

open System
open Avalonia.Controls

type Page = {
    Id: string
    Title: string
    Create: unit -> Control
}

/// Resources owned by an activation are released together when it stops.
type Registration =
    abstract Page: Page -> unit
    abstract Own: IDisposable -> unit

type IDesktopPlugin =
    abstract Activate: Registration -> unit
