namespace MyDebugTools.Samples

open Avalonia.Controls
open Avalonia.FuncUI.DSL
open Avalonia.FuncUI.VirtualDom
open MyDebugTools.Contracts

type Workbench() =
    interface IDesktopPlugin with
        member _.Activate registry =
            let options = [| "请求 A"; "请求 B" |]
            let selector = lazy (ComboBox(ItemsSource = options, SelectedIndex = 0))
            let selected () =
                if selector.IsValueCreated then string selector.Value.SelectedItem else options[0]
            registry.MenuPoint {
                PageId = "sample.workbench"; PointId = "selection"; Version = 1
                Selection = selected
            }
            registry.MenuPoint {
                PageId = "sample.workbench"; PointId = "summary"
#if SUMMARY_V2
                Version = 2
#else
                Version = 1
#endif
                Selection = selected
            }
            registry.Page {
                Id = "sample.workbench"
                Title = "工作台"
                Create = fun () ->
                    StackPanel.create [
                        StackPanel.spacing 10.
                        StackPanel.children [
                            TextBlock.create [ TextBlock.text "工作台"; TextBlock.fontSize 24. ]
                            ContentControl.create [ ContentControl.content selector.Value ]
                            TextBlock.create [ TextBlock.text "草稿" ]
                            TextBox.create [ TextBox.name "Draft"; TextBox.placeHolderText "输入待处理的内容" ]
                        ]
                    ] |> VirtualDom.create
            }
