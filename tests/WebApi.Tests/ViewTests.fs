module MyDebugTools.WebApi.Tests.ViewTests

open MyDebugTools.WebApi
open Xunit

[<Fact>]
let ``JSON format preserves values and locates invalid UTF8 input in editor characters``() =
    let source = "{\"值\":12345678901234567890123}"
    match Presentation.formatJson source with
    | Ok text -> Assert.Contains("\n", text); Assert.Contains("12345678901234567890123", text)
    | Error e -> failwith e.Message
    let broken = "{\r\n\"中文\":?}"
    match Presentation.formatJson broken with
    | Ok _ -> failwith "expected error"
    | Error e -> Assert.Equal(broken.IndexOf('?'), e.Offset); Assert.Contains("第 2 行", e.Message)
    Assert.Equal(".json", Presentation.language "application/problem+json; charset=utf-8")
    Assert.Equal(".xml", Presentation.language "application/example+xml")
    for extension in [".json"; ".xml"] do
        let rules = AvaloniaEdit.Highlighting.HighlightingManager.Instance.GetDefinitionByExtension(extension)
        Assert.NotNull(rules)
        Assert.NotEmpty(rules.MainRuleSet.Rules)

[<Fact>]
let ``XML formatting and HTML preview expose readable structure only``() =
    match Presentation.formatXml "<doc><id>2</id></doc>" with
    | Ok text -> Assert.Contains("\n", text)
    | Error e -> failwith e
    Assert.True(Result.isError(Presentation.formatXml "<broken>"))
    let html = "<h1>错误</h1><p>A &amp; B</p><script>throw 1</script><iframe src='/remote'></iframe><img src='/other'>"
    Assert.Equal<(string * string) array>([|"h1", "错误"; "p", "A & B"|], Presentation.htmlBlocks html)
