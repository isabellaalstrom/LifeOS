namespace LifeOS.Web.Ingress;

public class IngressOptions
{
    /// <summary>Honor the Home Assistant X-Ingress-Path header. Enable only when running as an HA app.</summary>
    public bool Enabled { get; set; }

    /// <summary>Peers allowed to supply X-Ingress-Path. 172.30.32.2 is the Supervisor ingress gateway.</summary>
    public string[] TrustedProxyAddresses { get; set; } = ["172.30.32.2"];
}
