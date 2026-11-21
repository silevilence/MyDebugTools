module internal MyDebugTools.DllDependencies.Graph

open System
open System.Collections.Generic
open System.IO
open Avalonia
open Avalonia.Automation
open Avalonia.Controls
open Avalonia.Controls.Primitives
open Avalonia.Layout
open Avalonia.Media
open MyDebugTools.DllDebugger

type Node =
    { Name: string; Detail: string; Column: int; Row: int }

type Link = { Source: int; Destination: int; Import: DependencyEdge }

// Breadth-first columns keep direct dependencies beside the root. Each module
// has one position even when several imports (or a cycle) lead to it.
let project (analysis: DependencyAnalysis) =
    let nodes = ResizeArray<Node>()
    let links = ResizeArray<Link>()
    let positions = Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    let heights = ResizeArray<int>()
    let queue = Queue<int * DependencyModule>()
    let add key name detail column =
        let index = nodes.Count
        if heights.Count = column then heights.Add 0
        nodes.Add { Name = name; Detail = detail; Column = column; Row = heights[column] }
        heights[column] <- heights[column] + 1
        positions.Add(key, index)
        index
    let root = analysis.Root
    queue.Enqueue(add root.Path (Path.GetFileName root.Path) (Labels.moduleLabel root) 0, root)
    while queue.Count > 0 do
        let source, m = queue.Dequeue()
        for edge in m.Imports do
            let target = edge.Target
            let key = if isNull target then edge.Name + "|" + edge.Problem else target.Path
            let destination =
                match positions.TryGetValue key with
                | true, index -> index
                | _ ->
                    let column = nodes[source].Column + 1
                    if isNull target then add key (edge.Name + " · 未解析") edge.Problem column
                    else
                        let index = add key (Path.GetFileName target.Path) (Labels.moduleLabel target) column
                        queue.Enqueue(index, target)
                        index
            links.Add { Source = source; Destination = destination; Import = edge }
    nodes.ToArray(), links.ToArray()

let private width, height = 164., 34.
let private origin node = Point(20. + 220. * float node.Column, 24. + 64. * float node.Row)

let private drawLinks (nodes: Node array) (pairs: Link array) active (context: DrawingContext) =
    let draw (edge: Link) =
        let fromNode, toNode = nodes[edge.Source], nodes[edge.Destination]
        let a, b = origin fromNode, origin toNode
        let start = Point(a.X + width, a.Y + height / 2.)
        let finish = Point(b.X, b.Y + height / 2.)
        let selected = edge.Source = active || edge.Destination = active
        let pen = Pen((if selected then Brushes.SteelBlue else Brushes.Gray), (if selected then 2. else 1.))
        let route =
            if toNode.Column = fromNode.Column + 1 then
                [| start; Point(start.X + 8., start.Y); Point(finish.X - 12., finish.Y); finish |]
            else
                // Return edges use the space above a row, including self loops.
                let y = min a.Y b.Y - 12.
                [| start; Point(start.X + 12., start.Y); Point(start.X + 12., y)
                   Point(finish.X - 12., y); Point(finish.X - 12., finish.Y); finish |]
        for i in 1..route.Length - 1 do context.DrawLine(pen, route[i-1], route[i])
        context.DrawLine(pen, Point(finish.X - 6., finish.Y - 4.), finish)
        context.DrawLine(pen, Point(finish.X - 6., finish.Y + 4.), finish)
    for edge in pairs do
        if edge.Source <> active && edge.Destination <> active then draw edge
    for edge in pairs do
        if edge.Source = active || edge.Destination = active then draw edge

let create (subscriptions: Subscriptions.Group) analysis =
    let nodes, links = project analysis
    let canvas = Canvas(Width = (nodes |> Array.maxBy _.Column |> origin).X + width + 20.,
                        Height = (nodes |> Array.maxBy _.Row |> origin).Y + height + 20.)
    let pairs = links |> Array.distinctBy (fun edge -> edge.Source, edge.Destination)
    let mutable active = 0
    let lines = new MyDebugTools.Contracts.DrawingSurface((fun ctx -> drawLinks nodes pairs active ctx),
                                                           Width = canvas.Width, Height = canvas.Height)
    subscriptions.Add lines
    canvas.Children.Add lines
    let details = StackPanel(Spacing = 3., Margin = Thickness(4.))
    let detail text = details.Children.Add(TextBlock(Text = text, TextWrapping = TextWrapping.Wrap))
    let buttons = ResizeArray<Button>()
    let select index =
        buttons[active].FontWeight <- FontWeight.Normal
        active <- index
        buttons[index].FontWeight <- FontWeight.Bold
        lines.InvalidateVisual()
        details.Children.Clear()
        detail nodes[index].Detail
        for link in links do
            if link.Source = index then detail $"导入 → {Labels.edgeLabel link.Import}"
            if link.Destination = index then detail $"来自 {nodes[link.Source].Detail} → {Labels.edgeLabel link.Import}"
    for index, node in Array.indexed nodes do
        let label = TextBlock(Text = node.Name, TextTrimming = TextTrimming.CharacterEllipsis)
        let button = Button(Content = label, Width = width, Height = height,
                            HorizontalContentAlignment = HorizontalAlignment.Center)
        AutomationProperties.SetName(button, node.Name)
        ToolTip.SetTip(button, node.Detail)
        let pos = origin node
        Canvas.SetLeft(button, pos.X)
        Canvas.SetTop(button, pos.Y)
        button.Click.Subscribe(fun _ -> select index) |> subscriptions.Add
        buttons.Add button
        canvas.Children.Add button
    select 0
    let layout = Grid(RowDefinitions = RowDefinitions("3*,2*"))
    let drawing = ScrollViewer(Content = canvas, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto)
    layout.Children.Add drawing
    let inspector = ScrollViewer(Content = details)
    Grid.SetRow(inspector, 1)
    layout.Children.Add inspector
    layout
