using Godot;
using Godot.Collections;

namespace Netfox.Extras;

/// <summary><para>Base class for creating responsive networked weapons in C#.</para>
/// <para>Wraps <c>_NetworkWeaponProxy</c> (GDScript) so all game logic can live
/// in type-safe C# while the proxy handles RPCs.</para>
/// <para>Extend this class and override <see cref="Spawn"/> (required) plus any
/// other virtual methods you need. The proxy child node is created automatically.</para>
/// </summary>
public abstract partial class NetworkWeapon : Node
{
    private GodotObject _proxy;

    public override void _Ready()
    {
        // Instantiate the GDScript proxy as a child so its RPCs are registered.
        // MUST use GDScript.New() — Script.Call("new") does not work for GDScript classes.
        var proxyScript = GD.Load<GDScript>("res://addons/netfox.extras/weapon/network-weapon-proxy.gd");
        _proxy = (GodotObject)proxyScript.New();
        var proxyNode = (Node)_proxy;

        // Deterministic name is CRITICAL — RPCs route by node path, so every peer
        // must have the same name for this node or RPCs silently fail.
        proxyNode.Name = "Proxy";
        AddChild(proxyNode);

        // Wire all callables to our C# virtual methods.
        _proxy.Set("c_can_fire",       Callable.From(CanFire));
        _proxy.Set("c_can_peer_use",   Callable.From<int, bool>(CanPeerUse));
        _proxy.Set("c_spawn",          Callable.From(Spawn));
        _proxy.Set("c_get_data",       Callable.From<Node, Dictionary>(GetData));
        _proxy.Set("c_apply_data",     Callable.From<Node, Dictionary>(ApplyData));
        _proxy.Set("c_is_reconcilable",Callable.From<Node, Dictionary, Dictionary, bool>(IsReconcilable));
        _proxy.Set("c_reconcile",      Callable.From<Node, Dictionary, Dictionary>(Reconcile));
        _proxy.Set("c_after_fire",     Callable.From<Node>(AfterFire));
    }

    // Propagate multiplayer authority changes to the proxy node.
    // The proxy's authority must match so is_multiplayer_authority() works correctly
    // in the GDScript RPC handlers.
    public override void _Notification(int what)
    {
        // There is no built-in notification for authority change in Godot 4,
        // so we do it manually in SetWeaponAuthority.
    }

    /// <summary>Must be called after SetMultiplayerAuthority on this node
    /// to propagate the authority to the internal proxy.</summary>
    public void SetWeaponAuthority(int peerId)
    {
        SetMultiplayerAuthority(peerId);
        if (_proxy is Node proxyNode)
            proxyNode.SetMultiplayerAuthority(peerId);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>Try to fire the weapon. Returns the spawned projectile, or
    /// <c>null</c> if <see cref="CanFire"/> returned false.</summary>
    public Node Fire()
    {
        var result = _proxy.Call("fire");
        if (result.VariantType == Variant.Type.Nil)
            return null;
        return (Node)result;
    }

    /// <summary>Returns the network tick on which the weapon was last fired.
    /// Useful for compensating latency when simulating projectiles after
    /// creation.</summary>
    public long GetFiredTick() => (long)_proxy.Call("get_fired_tick");

    // -------------------------------------------------------------------------
    // Virtual overrides — mirror the GDScript NetworkWeapon virtual methods
    // -------------------------------------------------------------------------

    /// <summary>Return <c>true</c> when the weapon is allowed to fire.
    /// Use this for cooldowns and ammo checks.</summary>
    protected virtual bool CanFire() => false;

    /// <summary>Return <c>true</c> if the given peer is authorised to fire
    /// this weapon. Defaults to allowing any peer.</summary>
    protected virtual bool CanPeerUse(int peerId) => true;

    /// <summary>Spawn and initialise a projectile node. <b>Must be overridden.</b>
    /// Return the spawned node.</summary>
    protected abstract Node Spawn();

    /// <summary>Serialise the projectile's initial state for network
    /// reconciliation. Called on both client and server.</summary>
    protected virtual Dictionary GetData(Node projectile) => new();

    /// <summary>Apply data received from the server to a freshly spawned
    /// projectile (used when a remote peer fires).</summary>
    protected virtual void ApplyData(Node projectile, Dictionary data) { }

    /// <summary>Return <c>false</c> to reject the projectile request (e.g.
    /// client position is too far from server position — anti-cheat).</summary>
    protected virtual bool IsReconcilable(Node projectile, Dictionary requestData, Dictionary localData) => true;

    /// <summary>Correct the local projectile to match the server's authoritative
    /// state after the round-trip.</summary>
    protected virtual void Reconcile(Node projectile, Dictionary localData, Dictionary remoteData) { }

    /// <summary>Called after a successful fire — reset cooldowns, play sounds,
    /// etc.</summary>
    protected virtual void AfterFire(Node projectile) { }
}
