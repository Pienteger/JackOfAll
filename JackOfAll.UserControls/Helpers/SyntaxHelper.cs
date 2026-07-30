using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;

namespace JackOfAll.UserControls.Helpers;

public static class SyntaxHelper
{
    private static bool _registered;

    public static void EnsureRegistered()
    {
        if (_registered) return;
        _registered = true;

        Register("JSON-Dark", JsonDarkXshd);
        Register("CSharp-Dark", CSharpDarkXshd);
        Register("JavaScript-Dark", JavaScriptDarkXshd);
        Register("Python-Dark", PythonDarkXshd);

        Register("JSON-Light", JsonLightXshd);
        Register("CSharp-Light", CSharpLightXshd);
        Register("JavaScript-Light", JavaScriptLightXshd);
        Register("Python-Light", PythonLightXshd);
    }

    public static bool IsDarkTheme()
    {
        try
        {
            var bg = Application.Current.FindResource("SurfaceBackground") as SolidColorBrush;
            if (bg is null) return true;
            var c = bg.Color;
            var luminance = 0.299 * c.R + 0.587 * c.G + 0.114 * c.B;
            return luminance < 128;
        }
        catch { return true; }
    }

    public static IHighlightingDefinition? ForExtension(string? ext)
    {
        EnsureRegistered();
        if (ext is null) return null;
        var suffix = IsDarkTheme() ? "Dark" : "Light";
        return ext.ToLowerInvariant() switch
        {
            ".json" => HighlightingManager.Instance.GetDefinition($"JSON-{suffix}"),
            ".cs" => HighlightingManager.Instance.GetDefinition($"CSharp-{suffix}"),
            ".js" => HighlightingManager.Instance.GetDefinition($"JavaScript-{suffix}"),
            ".py" => HighlightingManager.Instance.GetDefinition($"Python-{suffix}"),
            ".xml" or ".xaml" or ".xshd" or ".csproj" or ".slnx" => HighlightingManager.Instance.GetDefinition("XML"),
            ".html" or ".htm" => HighlightingManager.Instance.GetDefinition("HTML"),
            _ => null
        };
    }

    public static IHighlightingDefinition? ForLanguage(string language)
    {
        EnsureRegistered();
        var suffix = IsDarkTheme() ? "Dark" : "Light";
        return language switch
        {
            "csharp" => HighlightingManager.Instance.GetDefinition($"CSharp-{suffix}"),
            "javascript" => HighlightingManager.Instance.GetDefinition($"JavaScript-{suffix}"),
            "python" => HighlightingManager.Instance.GetDefinition($"Python-{suffix}"),
            _ => null
        };
    }

    private static void Register(string name, string xshd)
    {
        using var reader = new XmlTextReader(new System.IO.StringReader(xshd));
        var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
        HighlightingManager.Instance.RegisterHighlighting(name, [], def);
    }

    // ── Dark theme definitions (VS Code Dark+) ───────────────────

    private const string JsonDarkXshd = """
        <SyntaxDefinition name="JSON-Dark"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="String" foreground="#CE9178" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Bool" foreground="#569CD6" fontWeight="bold" />
          <Color name="Null" foreground="#569CD6" fontStyle="italic" />
          <Color name="Key" foreground="#9CDCFE" />
          <Color name="Punctuation" foreground="#D4D4D4" />
          <RuleSet ignoreCase="false">
            <Span color="Key" multiline="false">
              <Begin>"</Begin>
              <End>(?=\s*:)</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Keywords color="Bool">
              <Word>true</Word>
              <Word>false</Word>
            </Keywords>
            <Keywords color="Null">
              <Word>null</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE+\-]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\]:,]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string CSharpDarkXshd = """
        <SyntaxDefinition name="CSharp-Dark"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6A9955" />
          <Color name="String" foreground="#CE9178" />
          <Color name="Char" foreground="#CE9178" />
          <Color name="Keyword" foreground="#569CD6" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#C586C0" />
          <Color name="Type" foreground="#4EC9B0" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Preprocessor" foreground="#808080" />
          <Color name="Punctuation" foreground="#D4D4D4" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String" multiline="true">
              <Begin>@"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin='""' end="" />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Char">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Preprocessor" begin="#" />
            <Keywords color="Keyword">
              <Word>abstract</Word><Word>as</Word><Word>base</Word><Word>bool</Word>
              <Word>byte</Word><Word>class</Word><Word>const</Word><Word>decimal</Word>
              <Word>delegate</Word><Word>double</Word><Word>enum</Word><Word>event</Word>
              <Word>explicit</Word><Word>extern</Word><Word>false</Word><Word>fixed</Word>
              <Word>float</Word><Word>implicit</Word><Word>int</Word><Word>interface</Word>
              <Word>internal</Word><Word>lock</Word><Word>long</Word><Word>namespace</Word>
              <Word>new</Word><Word>null</Word><Word>object</Word><Word>operator</Word>
              <Word>out</Word><Word>override</Word><Word>params</Word><Word>private</Word>
              <Word>protected</Word><Word>public</Word><Word>readonly</Word><Word>record</Word>
              <Word>ref</Word><Word>sbyte</Word><Word>sealed</Word><Word>short</Word>
              <Word>sizeof</Word><Word>stackalloc</Word><Word>static</Word><Word>string</Word>
              <Word>struct</Word><Word>this</Word><Word>true</Word><Word>typeof</Word>
              <Word>uint</Word><Word>ulong</Word><Word>unchecked</Word><Word>unsafe</Word>
              <Word>ushort</Word><Word>using</Word><Word>var</Word><Word>virtual</Word>
              <Word>void</Word><Word>volatile</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>else</Word><Word>switch</Word><Word>case</Word>
              <Word>default</Word><Word>while</Word><Word>do</Word><Word>for</Word>
              <Word>foreach</Word><Word>in</Word><Word>break</Word><Word>continue</Word>
              <Word>goto</Word><Word>return</Word><Word>throw</Word><Word>try</Word>
              <Word>catch</Word><Word>finally</Word><Word>yield</Word><Word>await</Word>
              <Word>async</Word><Word>is</Word><Word>when</Word><Word>where</Word>
            </Keywords>
            <Keywords color="Type">
              <Word>Console</Word><Word>String</Word><Word>Int32</Word><Word>Boolean</Word>
              <Word>Task</Word><Word>List</Word><Word>Dictionary</Word><Word>IEnumerable</Word>
              <Word>Exception</Word><Word>Action</Word><Word>Func</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_fFdDmMuUlL]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string JavaScriptDarkXshd = """
        <SyntaxDefinition name="JavaScript-Dark"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6A9955" />
          <Color name="String" foreground="#CE9178" />
          <Color name="Keyword" foreground="#569CD6" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#C586C0" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Regex" foreground="#D16969" />
          <Color name="BuiltIn" foreground="#4EC9B0" />
          <Color name="Punctuation" foreground="#D4D4D4" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>`</Begin>
              <End>`</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Keywords color="Keyword">
              <Word>var</Word><Word>let</Word><Word>const</Word><Word>function</Word>
              <Word>class</Word><Word>extends</Word><Word>new</Word><Word>this</Word>
              <Word>super</Word><Word>typeof</Word><Word>instanceof</Word><Word>void</Word>
              <Word>delete</Word><Word>true</Word><Word>false</Word><Word>null</Word>
              <Word>undefined</Word><Word>static</Word><Word>async</Word><Word>of</Word>
              <Word>export</Word><Word>import</Word><Word>from</Word><Word>default</Word>
              <Word>get</Word><Word>set</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>else</Word><Word>switch</Word><Word>case</Word>
              <Word>while</Word><Word>do</Word><Word>for</Word><Word>in</Word>
              <Word>break</Word><Word>continue</Word><Word>return</Word><Word>throw</Word>
              <Word>try</Word><Word>catch</Word><Word>finally</Word><Word>yield</Word>
              <Word>await</Word><Word>with</Word>
            </Keywords>
            <Keywords color="BuiltIn">
              <Word>console</Word><Word>window</Word><Word>document</Word><Word>Math</Word>
              <Word>JSON</Word><Word>Promise</Word><Word>Array</Word><Word>Object</Word>
              <Word>String</Word><Word>Number</Word><Word>Boolean</Word><Word>Map</Word>
              <Word>Set</Word><Word>Date</Word><Word>Error</Word><Word>RegExp</Word>
              <Word>setTimeout</Word><Word>setInterval</Word><Word>fetch</Word>
              <Word>require</Word><Word>module</Word><Word>exports</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string PythonDarkXshd = """"
        <SyntaxDefinition name="Python-Dark"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#6A9955" />
          <Color name="String" foreground="#CE9178" />
          <Color name="Keyword" foreground="#569CD6" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#C586C0" />
          <Color name="BuiltIn" foreground="#4EC9B0" />
          <Color name="Decorator" foreground="#DCDCAA" />
          <Color name="Number" foreground="#B5CEA8" />
          <Color name="Punctuation" foreground="#D4D4D4" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="#" />
            <Span color="String" multiline="true">
              <Begin>"""</Begin>
              <End>"""</End>
            </Span>
            <Span color="String" multiline="true">
              <Begin>'''</Begin>
              <End>'''</End>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Decorator" begin="@" />
            <Keywords color="Keyword">
              <Word>and</Word><Word>as</Word><Word>assert</Word><Word>class</Word>
              <Word>def</Word><Word>del</Word><Word>global</Word><Word>import</Word>
              <Word>from</Word><Word>in</Word><Word>is</Word><Word>lambda</Word>
              <Word>nonlocal</Word><Word>not</Word><Word>or</Word><Word>pass</Word>
              <Word>with</Word><Word>True</Word><Word>False</Word><Word>None</Word>
              <Word>async</Word><Word>await</Word><Word>self</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>elif</Word><Word>else</Word><Word>for</Word>
              <Word>while</Word><Word>break</Word><Word>continue</Word><Word>return</Word>
              <Word>try</Word><Word>except</Word><Word>finally</Word><Word>raise</Word>
              <Word>yield</Word><Word>match</Word><Word>case</Word>
            </Keywords>
            <Keywords color="BuiltIn">
              <Word>print</Word><Word>len</Word><Word>range</Word><Word>type</Word>
              <Word>str</Word><Word>int</Word><Word>float</Word><Word>bool</Word>
              <Word>list</Word><Word>dict</Word><Word>set</Word><Word>tuple</Word>
              <Word>enumerate</Word><Word>zip</Word><Word>map</Word><Word>filter</Word>
              <Word>sorted</Word><Word>reversed</Word><Word>input</Word><Word>open</Word>
              <Word>isinstance</Word><Word>hasattr</Word><Word>getattr</Word><Word>setattr</Word>
              <Word>super</Word><Word>property</Word><Word>staticmethod</Word><Word>classmethod</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_jJ]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """";

    // ── Light theme definitions (VS Code Light+) ─────────────────

    private const string JsonLightXshd = """
        <SyntaxDefinition name="JSON-Light"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="String" foreground="#A31515" />
          <Color name="Number" foreground="#098658" />
          <Color name="Bool" foreground="#0000FF" fontWeight="bold" />
          <Color name="Null" foreground="#0000FF" fontStyle="italic" />
          <Color name="Key" foreground="#0451A5" />
          <Color name="Punctuation" foreground="#333333" />
          <RuleSet ignoreCase="false">
            <Span color="Key" multiline="false">
              <Begin>"</Begin>
              <End>(?=\s*:)</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Keywords color="Bool">
              <Word>true</Word>
              <Word>false</Word>
            </Keywords>
            <Keywords color="Null">
              <Word>null</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE+\-]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\]:,]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string CSharpLightXshd = """
        <SyntaxDefinition name="CSharp-Light"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#008000" />
          <Color name="String" foreground="#A31515" />
          <Color name="Char" foreground="#A31515" />
          <Color name="Keyword" foreground="#0000FF" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#8F08C4" />
          <Color name="Type" foreground="#267F99" />
          <Color name="Number" foreground="#098658" />
          <Color name="Preprocessor" foreground="#808080" />
          <Color name="Punctuation" foreground="#333333" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String" multiline="true">
              <Begin>@"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin='""' end="" />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Char">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Preprocessor" begin="#" />
            <Keywords color="Keyword">
              <Word>abstract</Word><Word>as</Word><Word>base</Word><Word>bool</Word>
              <Word>byte</Word><Word>class</Word><Word>const</Word><Word>decimal</Word>
              <Word>delegate</Word><Word>double</Word><Word>enum</Word><Word>event</Word>
              <Word>explicit</Word><Word>extern</Word><Word>false</Word><Word>fixed</Word>
              <Word>float</Word><Word>implicit</Word><Word>int</Word><Word>interface</Word>
              <Word>internal</Word><Word>lock</Word><Word>long</Word><Word>namespace</Word>
              <Word>new</Word><Word>null</Word><Word>object</Word><Word>operator</Word>
              <Word>out</Word><Word>override</Word><Word>params</Word><Word>private</Word>
              <Word>protected</Word><Word>public</Word><Word>readonly</Word><Word>record</Word>
              <Word>ref</Word><Word>sbyte</Word><Word>sealed</Word><Word>short</Word>
              <Word>sizeof</Word><Word>stackalloc</Word><Word>static</Word><Word>string</Word>
              <Word>struct</Word><Word>this</Word><Word>true</Word><Word>typeof</Word>
              <Word>uint</Word><Word>ulong</Word><Word>unchecked</Word><Word>unsafe</Word>
              <Word>ushort</Word><Word>using</Word><Word>var</Word><Word>virtual</Word>
              <Word>void</Word><Word>volatile</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>else</Word><Word>switch</Word><Word>case</Word>
              <Word>default</Word><Word>while</Word><Word>do</Word><Word>for</Word>
              <Word>foreach</Word><Word>in</Word><Word>break</Word><Word>continue</Word>
              <Word>goto</Word><Word>return</Word><Word>throw</Word><Word>try</Word>
              <Word>catch</Word><Word>finally</Word><Word>yield</Word><Word>await</Word>
              <Word>async</Word><Word>is</Word><Word>when</Word><Word>where</Word>
            </Keywords>
            <Keywords color="Type">
              <Word>Console</Word><Word>String</Word><Word>Int32</Word><Word>Boolean</Word>
              <Word>Task</Word><Word>List</Word><Word>Dictionary</Word><Word>IEnumerable</Word>
              <Word>Exception</Word><Word>Action</Word><Word>Func</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_fFdDmMuUlL]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string JavaScriptLightXshd = """
        <SyntaxDefinition name="JavaScript-Light"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#008000" />
          <Color name="String" foreground="#A31515" />
          <Color name="Keyword" foreground="#0000FF" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#8F08C4" />
          <Color name="Number" foreground="#098658" />
          <Color name="Regex" foreground="#811F3F" />
          <Color name="BuiltIn" foreground="#267F99" />
          <Color name="Punctuation" foreground="#333333" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="//" />
            <Span color="Comment" multiline="true" begin="/\*" end="\*/" />
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>`</Begin>
              <End>`</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Keywords color="Keyword">
              <Word>var</Word><Word>let</Word><Word>const</Word><Word>function</Word>
              <Word>class</Word><Word>extends</Word><Word>new</Word><Word>this</Word>
              <Word>super</Word><Word>typeof</Word><Word>instanceof</Word><Word>void</Word>
              <Word>delete</Word><Word>true</Word><Word>false</Word><Word>null</Word>
              <Word>undefined</Word><Word>static</Word><Word>async</Word><Word>of</Word>
              <Word>export</Word><Word>import</Word><Word>from</Word><Word>default</Word>
              <Word>get</Word><Word>set</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>else</Word><Word>switch</Word><Word>case</Word>
              <Word>while</Word><Word>do</Word><Word>for</Word><Word>in</Word>
              <Word>break</Word><Word>continue</Word><Word>return</Word><Word>throw</Word>
              <Word>try</Word><Word>catch</Word><Word>finally</Word><Word>yield</Word>
              <Word>await</Word><Word>with</Word>
            </Keywords>
            <Keywords color="BuiltIn">
              <Word>console</Word><Word>window</Word><Word>document</Word><Word>Math</Word>
              <Word>JSON</Word><Word>Promise</Word><Word>Array</Word><Word>Object</Word>
              <Word>String</Word><Word>Number</Word><Word>Boolean</Word><Word>Map</Word>
              <Word>Set</Word><Word>Date</Word><Word>Error</Word><Word>RegExp</Word>
              <Word>setTimeout</Word><Word>setInterval</Word><Word>fetch</Word>
              <Word>require</Word><Word>module</Word><Word>exports</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """;

    private const string PythonLightXshd = """"
        <SyntaxDefinition name="Python-Light"
            xmlns="http://icsharpcode.net/sharpdevelop/syntaxdefinition/2008">
          <Color name="Comment" foreground="#008000" />
          <Color name="String" foreground="#A31515" />
          <Color name="Keyword" foreground="#0000FF" fontWeight="bold" />
          <Color name="ControlKeyword" foreground="#8F08C4" />
          <Color name="BuiltIn" foreground="#267F99" />
          <Color name="Decorator" foreground="#795E26" />
          <Color name="Number" foreground="#098658" />
          <Color name="Punctuation" foreground="#333333" />
          <RuleSet ignoreCase="false">
            <Span color="Comment" begin="#" />
            <Span color="String" multiline="true">
              <Begin>"""</Begin>
              <End>"""</End>
            </Span>
            <Span color="String" multiline="true">
              <Begin>'''</Begin>
              <End>'''</End>
            </Span>
            <Span color="String">
              <Begin>"</Begin>
              <End>"</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="String">
              <Begin>'</Begin>
              <End>'</End>
              <RuleSet>
                <Span begin="\\" end="." />
              </RuleSet>
            </Span>
            <Span color="Decorator" begin="@" />
            <Keywords color="Keyword">
              <Word>and</Word><Word>as</Word><Word>assert</Word><Word>class</Word>
              <Word>def</Word><Word>del</Word><Word>global</Word><Word>import</Word>
              <Word>from</Word><Word>in</Word><Word>is</Word><Word>lambda</Word>
              <Word>nonlocal</Word><Word>not</Word><Word>or</Word><Word>pass</Word>
              <Word>with</Word><Word>True</Word><Word>False</Word><Word>None</Word>
              <Word>async</Word><Word>await</Word><Word>self</Word>
            </Keywords>
            <Keywords color="ControlKeyword">
              <Word>if</Word><Word>elif</Word><Word>else</Word><Word>for</Word>
              <Word>while</Word><Word>break</Word><Word>continue</Word><Word>return</Word>
              <Word>try</Word><Word>except</Word><Word>finally</Word><Word>raise</Word>
              <Word>yield</Word><Word>match</Word><Word>case</Word>
            </Keywords>
            <Keywords color="BuiltIn">
              <Word>print</Word><Word>len</Word><Word>range</Word><Word>type</Word>
              <Word>str</Word><Word>int</Word><Word>float</Word><Word>bool</Word>
              <Word>list</Word><Word>dict</Word><Word>set</Word><Word>tuple</Word>
              <Word>enumerate</Word><Word>zip</Word><Word>map</Word><Word>filter</Word>
              <Word>sorted</Word><Word>reversed</Word><Word>input</Word><Word>open</Word>
              <Word>isinstance</Word><Word>hasattr</Word><Word>getattr</Word><Word>setattr</Word>
              <Word>super</Word><Word>property</Word><Word>staticmethod</Word><Word>classmethod</Word>
            </Keywords>
            <Rule color="Number">
              \b[\d][\d.eE_jJ]*\b
            </Rule>
            <Rule color="Punctuation">
              [{}()\[\];:.,?!&amp;|^~=&lt;&gt;+\-*/%]
            </Rule>
          </RuleSet>
        </SyntaxDefinition>
        """";
}
