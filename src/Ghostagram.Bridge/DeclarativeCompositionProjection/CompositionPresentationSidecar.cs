using Ghostworx.System.Composition;

namespace Ghostagram.Bridge.DeclarativeCompositionProjection;

public sealed record CompositionLayoutEntry(string SourceIdentity, double X, double Y, double Width, double Height);
public sealed record CompositionWaypointEntry(string SourceIdentity, int Order, double X, double Y);
public sealed record CompositionDisplayEntry(string SourceIdentity, bool Collapsed);
public sealed record CompositionViewport(double X = 0, double Y = 0, double Zoom = 1);

/// <summary>Presentation-only state with copied collections and no extensible callback/metadata bag.</summary>
public sealed class CompositionPresentationSidecar
{
    private CompositionPresentationSidecar(long revision, CompositionLayoutEntry[] layout,
        CompositionWaypointEntry[] waypoints, CompositionDisplayEntry[] display, string[] selection,
        CompositionViewport viewport, int rawEntryCount)
    {
        Revision = revision;
        Layout = Array.AsReadOnly(layout);
        Waypoints = Array.AsReadOnly(waypoints);
        Display = Array.AsReadOnly(display);
        Selection = Array.AsReadOnly(selection);
        Viewport = viewport;
        RawEntryCount = rawEntryCount;
    }

    public long Revision { get; }
    public IReadOnlyList<CompositionLayoutEntry> Layout { get; }
    public IReadOnlyList<CompositionWaypointEntry> Waypoints { get; }
    public IReadOnlyList<CompositionDisplayEntry> Display { get; }
    public IReadOnlyList<string> Selection { get; }
    public CompositionViewport Viewport { get; }
    public int RawEntryCount { get; }

    /// <summary>
    /// Admits raw entries before a projector can discard orphans. The same admitted
    /// operation context must govern subsequent projection and terminal publication.
    /// </summary>
    public static CompositionSidecarAdmission Admit(long revision,
        IEnumerable<CompositionLayoutEntry> layout, IEnumerable<CompositionWaypointEntry> waypoints,
        IEnumerable<CompositionDisplayEntry> display, IEnumerable<string> selection,
        CompositionViewport? viewport, CompositionLimitProfile profile,
        CompositionOperationContext context, TimeProvider? timeProvider = null,
        string presentationProfile = CompositionPresentationProfile.Identity)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(context);
        var clock = timeProvider ?? TimeProvider.System;
        var count = 0;
        var textBytes = 0L;
        try
        {
            Check();
            if (revision < 0 || presentationProfile != CompositionPresentationProfile.Identity)
                return Failure("PROFILE", "presentation", "Invalid revision or unsupported presentation profile.");
            var positions = Copy(layout, item => { Identity(item.SourceIdentity); Geometry(item.X, item.Y);
                if (!Coordinate(item.Width) || !Coordinate(item.Height) || item.Width <= 0 || item.Height <= 0)
                    throw new SidecarFailure("GEOMETRY"); }, item => item.SourceIdentity);
            var points = Copy(waypoints, item => { Identity(item.SourceIdentity); Geometry(item.X, item.Y);
                if (item.Order < 0) throw new SidecarFailure("GEOMETRY"); },
                item => CompositionDiagramIdentity.Encode("waypoint", item.SourceIdentity, item.Order.ToString(global::System.Globalization.CultureInfo.InvariantCulture)));
            var hints = Copy(display, item => Identity(item.SourceIdentity), item => item.SourceIdentity);
            var selected = Copy(selection, Identity, item => item);
            var view = viewport ?? new CompositionViewport();
            if (viewport is not null) Entry();
            Geometry(view.X, view.Y);
            if (!double.IsFinite(view.Zoom) || view.Zoom <= 0 || view.Zoom > CompositionPresentationProfile.MaximumCoordinateMagnitude)
                throw new SidecarFailure("GEOMETRY");
            Check();
            return CompositionSidecarAdmission.Accept(new(revision, positions, points, hints, selected, view, count));
        }
        catch (SidecarFailure failure) { return Failure(failure.Code, "sidecar", "Presentation input failed bounded admission."); }
        catch (OperationCanceledException) { return Failure("CANCELLED", "sidecar", "Projection was cancelled."); }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or OverflowException)
        { return Failure("MALFORMED", "sidecar", "Presentation input could not be read completely."); }

        void Check()
        {
            var now = clock.GetUtcNow();
            context.Cancellation.ThrowIfCancellationRequested();
            if (now >= context.AbsoluteDeadlineUtc) throw new SidecarFailure("DEADLINE");
        }
        void Entry()
        {
            Check();
            if (++count > CompositionPresentationProfile.MaximumRawSidecarEntries) throw new SidecarFailure("BOUND");
        }
        void Identity(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) throw new SidecarFailure("IDENTITY");
            var size = global::System.Text.Encoding.UTF8.GetByteCount(value);
            if (size > CompositionDiagramIdentity.MaximumSourceKeyUtf8Bytes(profile)) throw new SidecarFailure("BOUND");
            textBytes = checked(textBytes + size);
            if (textBytes > CompositionPresentationProfile.MaximumProjectionUtf8Bytes) throw new SidecarFailure("BOUND");
        }
        static bool Coordinate(double value) => double.IsFinite(value) &&
            Math.Abs(value) <= CompositionPresentationProfile.MaximumCoordinateMagnitude;
        static void Geometry(double x, double y)
        {
            if (!Coordinate(x) || !Coordinate(y)) throw new SidecarFailure("GEOMETRY");
        }
        T[] Copy<T>(IEnumerable<T> source, Action<T> validate, Func<T, string> key)
        {
            if (source is null) throw new SidecarFailure("MALFORMED");
            var result = new List<T>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in source)
            {
                Entry();
                if (item is null) throw new SidecarFailure("MALFORMED");
                validate(item);
                if (!identities.Add(key(item))) throw new SidecarFailure("COLLISION");
                result.Add(item);
            }
            return result.OrderBy(key, StringComparer.Ordinal).ToArray();
        }
        static CompositionSidecarAdmission Failure(string code, string path, string message) =>
            CompositionSidecarAdmission.Reject(new("GRAM-COMP-" + code, path, message));
    }

    private sealed class SidecarFailure(string code) : Exception
    {
        public string Code { get; } = code;
    }
}

public sealed class CompositionSidecarAdmission
{
    private CompositionSidecarAdmission(CompositionPresentationSidecar? sidecar, CompositionProjectionDiagnostic? diagnostic)
    { Sidecar = sidecar; Diagnostic = diagnostic; }
    public bool IsAccepted => Sidecar is not null;
    public CompositionPresentationSidecar? Sidecar { get; }
    public CompositionProjectionDiagnostic? Diagnostic { get; }
    internal static CompositionSidecarAdmission Accept(CompositionPresentationSidecar value) => new(value, null);
    internal static CompositionSidecarAdmission Reject(CompositionProjectionDiagnostic value) => new(null, value);
}
