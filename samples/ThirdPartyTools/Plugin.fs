namespace ThirdParty

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts

type Tools() =
    interface IDesktopPlugin with
        member _.Activate registry =
            let edition = PrivateText.edition()
            let pageId = "thirdparty." + edition
            let title = "第三方 " + edition
            registry.Page {
                Id = pageId; Title = title
                Create = fun () -> TextBlock.create [ TextBlock.text (title + " " + PrivateText.version()) ] |> VirtualDom.create
            }
            registry.MenuAction {
                PageId = "sample.workbench"; PointId = "selection"; MinVersion = 1; MaxVersion = 1
                Title = title
                Invoke = fun selected -> $"{edition} {PrivateText.version()} · {selected}"
            }
