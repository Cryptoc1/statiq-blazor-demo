using Statiq.Razor;

namespace StatiqBlazor.Generator;

public sealed class DefaultPipeline : Pipeline
{
    public DefaultPipeline( )
    {
        InputModules = new()
        {
            new ReadFiles("content/index.json"),
        };

        ProcessModules = new()
        {
            new CacheDocuments
            {
                new SetDestination(Config.FromDocument((doc, ctx) => new NormalizedPath("index.html"))),

                new ParseJson(),
                new MergeContent(new ReadFiles("Index.cshtml")),
                new RenderRazor(),
            }
        };

        OutputModules = new()
        {
            new CopyStaticWebAssets(),
            new WriteFiles()
        };
    }
}