using Tsundosika.LimbusAssistant.CaptureProbe;

var options = ProbeOptions.Parse(args);
if (options is null)
{
    Console.WriteLine(ProbeOptions.Usage);
    return 1;
}
return await new ProbeSession(options).RunAsync();
