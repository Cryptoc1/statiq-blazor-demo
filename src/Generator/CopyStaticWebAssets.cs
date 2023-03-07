using System.Reflection;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using StatiqModule = Statiq.Common.Module;

namespace StatiqBlazor.Generator;

public sealed class CopyStaticWebAssets : StatiqModule
{
    private readonly string assemblyName;

    public CopyStaticWebAssets( string? assemblyName = null )
        => this.assemblyName = assemblyName
            ?? Assembly.GetEntryAssembly()?.GetName()?.Name
                ?? throw new ArgumentException( null, nameof( assemblyName ) );

    protected override async Task<IEnumerable<IDocument>> ExecuteContextAsync( IExecutionContext context )
    {
        var manifest = await StaticWebAssetsManifest.LoadAsync( assemblyName );
        if( manifest is null )
        {
            context.ManifestNotLoaded( assemblyName );
        }
        else if( manifest.ContentRoots.Length > 0 && manifest.Root is not null )
        {
            IDocument?[] documents;
            using( var semaphore = new SemaphoreSlim( 20, 20 ) )
            {
                var writes = manifest.EnumerateAssets()
                    .Select( asset => new Document( Path.Combine( manifest.ContentRoots[ asset.ContentRootIndex ], asset.SubPath ), asset.SubPath ) )
                    .Select( document => WriteAsync( document, context, semaphore ) );

                documents = await Task.WhenAll( writes );
            }

            return context.Inputs.Concat( documents.Where( document => document is not null ) )!;
        }

        return context.Inputs;
    }

    private static async Task<IDocument?> WriteAsync( Document document, IExecutionContext context, SemaphoreSlim semaphore )
    {
        await semaphore.WaitAsync( context.CancellationToken );

        try
        {
            var input = context.FileSystem.GetInputFile( document.Source );
            if( input is not null )
            {
                int hash = await input.GetCacheCodeAsync();
                var output = context.FileSystem.GetOutputFile( document.Destination );

                // do we actually need to copy?
                if( context.FileSystem.WriteTracker.TryGetPreviousWrite( output.Path, out int previousWriteHash ) && previousWriteHash == await output.GetCacheCodeAsync() )
                {
                    if( context.FileSystem.WriteTracker.TryGetPreviousContent( output.Path, out int previousHash ) && previousHash == hash )
                    {
                        context.AssetNotCopied( input.Path, output.Path );

                        context.FileSystem.WriteTracker.TrackWrite( output.Path, previousWriteHash, false );
                        context.FileSystem.WriteTracker.TrackContent( output.Path, previousHash );
                        return document;
                    }
                }

                await input.CopyToAsync( output, true, true, context.CancellationToken );

                context.FileSystem.WriteTracker.TrackContent( output.Path, hash );
                context.AssetCopied( input.Path, output.Path );

                return document;
            }
        }
        finally
        {
            _ = semaphore.Release();
        }

        return null;
    }

    private sealed record class StaticWebAssetsManifest( string[] ContentRoots, StaticWebAssetManifest Root )
    {
        private const string FileExtension = ".staticwebassets.runtime.json";

        public static async ValueTask<StaticWebAssetsManifest?> LoadAsync( string assemblyName )
        {
            ArgumentException.ThrowIfNullOrEmpty( assemblyName );

            string path = Path.Combine( AppDomain.CurrentDomain.BaseDirectory, assemblyName + FileExtension );
            if( !File.Exists( path ) )
            {
                return null;
            }

            using var file = File.OpenRead( path );
            return await JsonSerializer.DeserializeAsync<StaticWebAssetsManifest>( file );
        }

        public IEnumerable<StaticWebAsset> EnumerateAssets( )
        {
            return Assets( Root );
            static IEnumerable<StaticWebAsset> Assets( StaticWebAssetManifest manifest )
            {
                if( manifest.Asset is not null )
                {
                    yield return manifest.Asset;
                }

                if( manifest.Children is not null )
                {
                    foreach( var asset in manifest.Children.Values.SelectMany( Assets ) )
                    {
                        yield return asset;
                    }
                }
            }
        }
    }

    private sealed record class StaticWebAssetManifest( Dictionary<string, StaticWebAssetManifest> Children, StaticWebAsset Asset );
    private sealed record class StaticWebAsset( int ContentRootIndex, string SubPath );

}

public static partial class CopyStaticWebAssetsLogging
{
    [LoggerMessage( Level = LogLevel.Debug, Message = "Static Web Asset was not copied from '{source}' to '{destination}', file has not changed." )]
    public static partial void AssetNotCopied( this IExecutionContext context, NormalizedPath source, NormalizedPath destination );

    [LoggerMessage( Level = LogLevel.Debug, Message = "Static Web Asset was copied from '{source}' to '{destination}'." )]
    public static partial void AssetCopied( this IExecutionContext context, NormalizedPath source, NormalizedPath destination );

    [LoggerMessage( Level = LogLevel.Information, Message = "Failed to load manifest file for assembly '{assemblyName}'" )]
    public static partial void ManifestNotLoaded( this IExecutionContext context, string assemblyName );
}
