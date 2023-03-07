await Bootstrapper.Factory.CreateDefault( args )
    .AddHostingCommands()
    .AddSetting( Keys.CleanMode, CleanMode.Self )
    .AddSetting( Keys.Host, "localhost" )
    .AddSetting( Keys.LinkHideIndexPages, true )
    .AddSetting( Keys.LinkHideExtensions, true )
    .AddSetting( Keys.LinkLowercase, true )
    .AddSetting( WebKeys.Xref, Config.FromDocument( doc => doc.GetTitle()?.Replace( ' ', '-' ) ) )
    .RunAsync();

