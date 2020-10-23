// Learn more about F# at http://fsharp.org

open System
open System.Diagnostics
open System.IO
open System.Security.Cryptography
open CliWrap

type Msg =
    | SetWallpaper of string
    | SetRandomWallpaper

let getProcessor (randomPicture: unit -> Option<string>) =
    MailboxProcessor<Msg>
        .Start(fun inbox ->
            let rec innerLoop () =
                async {
                    do! Async.SwitchToThreadPool()
                    let! msg = inbox.Receive()

                    match msg with
                    | SetWallpaper path ->
                        let args =
                            let str =
                                sprintf """set org.gnome.desktop.background picture-uri "%s" """ path

                            str.TrimEnd()

                        printfn "Executing [/usr/bin/gsettings %s]" args

                        let! result =
                            Cli
                                .Wrap("/usr/bin/gsettings")
                                .WithArguments(args)
                                .ExecuteAsync()
                                .Task
                            |> Async.AwaitTask

                        printfn "Changed Picture At: %s\n" (DateTime.Now.ToLongTimeString())

                        if result.ExitCode <> 0
                        then eprintfn "Couldn't change the Picture, will retry later..."
                    | SetRandomWallpaper ->
                        match randomPicture () with
                        | Some path -> inbox.Post(SetWallpaper path)
                        | None -> eprintfn "No Pictures were found, will check again later"

                    return! innerLoop ()
                }

            innerLoop ())


let refresh (argv: array<string>) =
    argv
    |> Array.tryFind (fun str -> str.Contains("--refresh="))
    |> Option.map (fun refresh -> refresh.Substring("--refresh=".Length))
    |> Option.orElse
        (Environment.GetEnvironmentVariable("BgPictureChanger_REFRESH_RATE")
         |> Option.ofObj)
    |> Option.orElse (Some "1h")
    |> Option.map (fun str ->
        let str = if str.Length <= 1 then "1h" else str

        match str.[str.Length - 1] with
        | 's' ->
            match Decimal.TryParse(str.Substring(0, str.Length - 1) |> string) with
            | true, value ->
                TimeSpan
                    .FromSeconds(value |> float)
                    .TotalMilliseconds
            | false, _ -> TimeSpan.FromSeconds(30.).TotalMilliseconds
        | 'm' ->
            match Decimal.TryParse(str.Substring(0, str.Length - 1) |> string) with
            | true, value ->
                TimeSpan
                    .FromMinutes(value |> float)
                    .TotalMilliseconds
            | false, _ -> TimeSpan.FromMinutes(30.).TotalMilliseconds
        | 'h' ->
            match Decimal.TryParse(str.Substring(0, str.Length - 1) |> string) with
            | true, value ->
                TimeSpan
                    .FromHours(value |> float)
                    .TotalMilliseconds
            | false, _ -> TimeSpan.FromHours(1.).TotalMilliseconds
        | 'd' ->
            match Decimal.TryParse(str.Substring(0, str.Length - 1) |> string) with
            | true, value ->
                TimeSpan
                    .FromDays(value |> float)
                    .TotalMilliseconds
            | false, _ -> TimeSpan.FromDays(1.).TotalMilliseconds
        | 'w' ->
            match Decimal.TryParse(str.Substring(0, str.Length - 1) |> string) with
            | true, value ->
                TimeSpan
                    .FromDays((value |> float) * 7.)
                    .TotalMilliseconds
            | false, _ -> TimeSpan.FromDays(7.).TotalMilliseconds
        | _ -> TimeSpan.FromHours(1.).TotalMilliseconds)
    |> Option.get

let picturesDir (argv: array<string>) =
    argv
    |> Array.tryFind (fun str -> str.Contains("--pictures="))
    |> Option.map (fun pathlike -> pathlike.Substring("--pictures=".Length))
    |> Option.orElse
        (Environment.GetEnvironmentVariable("BgPictureChanger_Pictures_PATH")
         |> Option.ofObj)
    |> Option.orElse
        (Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
         |> Option.ofObj)
    |> Option.orElse
        (Environment.GetEnvironmentVariable("HOME")
         |> Option.ofObj
         |> Option.map (fun home -> sprintf "%s/Pictures/" home))
    |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
    |> Option.get

let getWriter (argv: array<string>) =
    let path =
        argv
        |> Array.tryFind (fun str -> str.Contains("--logpath="))
        |> Option.map (fun pathlike -> pathlike.Substring("--logpath=".Length))
        |> Option.orElse
            (Environment.GetEnvironmentVariable("HOME")
             |> Option.ofObj
             |> Option.map (fun home -> sprintf "%s/.config/.changebg.log" home))
        |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
        |> Option.get

    try
        new StreamWriter(File.Open(path, FileMode.Append))
    with :? DirectoryNotFoundException -> File.CreateText(path)

let getPath (file: FileInfo) = sprintf "file://%s" file.FullName

let getRandomIndex (fileCount: int) =
    RandomNumberGenerator.GetInt32(fileCount)

let getDirInfo (path: string) =
    try
        let dirInfo = DirectoryInfo(Path.GetFullPath(path))
        dirInfo.EnumerateDirectories() |> ignore
        dirInfo
    with :? DirectoryNotFoundException as ex ->
        eprintfn "\n%O\nDirectory Not Found, Falling back to Home Pictures..." ex

        let picturesDir =
            Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
            |> Option.ofObj
            |> Option.map (fun pathlike -> Path.GetFullPath(pathlike))
            |> Option.get

        DirectoryInfo(Path.GetFullPath(picturesDir))

let rec changePicture (refresh: int) (processor: MailboxProcessor<Msg>) =
    async {
        do! Async.SwitchToThreadPool()
        printfn "Running in process: [%i]" (Process.GetCurrentProcess().Id)
        // TODO: Add posibility to read config file from directory with wallpaper order
        // processor.Post (SetWallpaper nextWallPaper)
        processor.Post SetRandomWallpaper
        do! Async.Sleep refresh
        return! changePicture refresh processor
    }

let getRandomPicture (dirInfo: DirectoryInfo) =
    fun () ->
        let pictures =
            try
                dirInfo.EnumerateFiles()
            with :? DirectoryNotFoundException -> Seq.empty

        if pictures |> Seq.length <= 0 then
            None
        else
            let index = getRandomIndex (pictures |> Seq.length)
            Some(getPath (pictures |> Seq.item index))

[<EntryPoint>]
let main argv =
    let refresh = refresh argv
    let picturesDir = picturesDir argv
    let dirInfo = getDirInfo (picturesDir)
    let processor = getProcessor (getRandomPicture dirInfo)

    use writer = getWriter argv

    writer.AutoFlush <- true
    Console.SetOut(writer)
    Console.SetError(writer)
    printfn "Starting Program [%s - %s]" (DateTime.Now.ToLongDateString()) (DateTime.Now.ToLongTimeString())

    try
        changePicture (refresh |> int) processor
        |> Async.RunSynchronously
    with ex ->
        writer.Flush()
        writer.Dispose()
        let stderr = new StreamWriter(Console.OpenStandardError());
        let stdout = new StreamWriter(Console.OpenStandardOutput());
        stdout.AutoFlush <- true;
        stderr.AutoFlush <- true;
        Console.SetOut(stdout);
        Console.SetError(stderr);
        eprintfn "Unhandled error\n\n%O" ex
    0
