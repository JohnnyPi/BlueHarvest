using BlueHarvest.Sim.Actors;
using BlueHarvest.Sim.Lighting;
using BlueHarvest.Sim.Perception;
using BlueHarvest.Sim.World;

namespace BlueHarvest.Sim.Ai;

/// <summary>
/// A creature's memory of contacts, built only from what its senses delivered (Percepts) and what
/// its own jaws felt. Nothing here reads the true state of the world.
/// </summary>
public sealed class ContactMemory
{
    private readonly List<Contact> _contacts = new();

    public ContactMemory(AiProfile profile) => Profile = profile;

    public AiProfile Profile { get; }

    /// <summary>Everything remembered, oldest first.</summary>
    public IReadOnlyList<Contact> All => _contacts;

    public Contact? Find(ContactKey key)
    {
        foreach (var contact in _contacts)
        {
            if (contact.Key == key)
                return contact;
        }
        return null;
    }

    /// <summary>
    /// Folds one turn's percepts into memory. Noises and lights first, then sight, so when several
    /// cues name the same contact the sharpest (sight) sets the estimate.
    /// </summary>
    public void Observe(Simulation sim, Actor self, Percepts percepts)
    {
        long now = sim.CurrentTick;
        var eye = sim.EyeTile(self);

        foreach (var heard in percepts.Heard)
        {
            var noise = heard.Noise;
            var key = noise.ActorId is { } id
                ? ContactKey.ForActor(id)
                : new ContactKey(ContactSource.Sound, noise.Position.Y * sim.Map.Width + noise.Position.X);
            var contact = Touch(key, now);

            int distance = (int)Math.Sqrt(Distance2(eye, noise.Position));
            int confidence = Math.Min(Profile.HeardConfidenceCap, heard.Perceived * Profile.HeardConfidencePerPoint);
            if (noise.Kind == NoiseKinds.Scream && contact.Class != ContactClass.Object)
            {
                contact.Class = ContactClass.Prey;
                confidence = 100;
            }
            Refresh(contact, now, noise.Position, sim.Terrain[noise.Position], confidence, uncertainty: 1 + distance / 8,
                $"heard {noise.Kind}");
        }

        foreach (var seen in percepts.Lights)
        {
            if (seen.Score < Profile.LightInterestThreshold)
                continue;
            int index = IndexOf(sim.Lights, seen.Light);
            if (index < 0)
                continue;
            var contact = Touch(new ContactKey(ContactSource.Light, index), now);
            Refresh(contact, now, seen.Light.Position, sim.Terrain[seen.Light.Position], seen.Score, uncertainty: 0, "a light");
        }

        int threshold = self.Species.Senses.DetectThreshold;
        foreach (var seen in percepts.Seen)
        {
            var target = seen.Target;
            if (seen.Detected)
            {
                var contact = Touch(ContactKey.ForActor(target.Id), now);
                if (contact.Class != ContactClass.Object)
                    contact.Class = IsPrey(target) ? ContactClass.Prey : ContactClass.Object;
                Refresh(contact, now, target.Position, sim.StandHeight(target), Math.Min(100, 50 + seen.Score),
                    uncertainty: 0, "seen");
                contact.Moving = seen.Moving;
            }
            else if (seen.Score * 2 >= threshold)
            {
                var contact = Touch(ContactKey.ForActor(target.Id), now);
                Refresh(contact, now, target.Position, sim.StandHeight(target), Math.Min(Profile.GlimpseConfidenceCap, seen.Score * 3),
                    uncertainty: 1, "glimpsed");
                contact.Moving = seen.Moving;
            }
        }
    }

    /// <summary>
    /// Drops contacts whose confidence has faded to nothing, then the weakest ones if memory is full.
    /// Contacts identified as objects are kept, so the same pole light doesn't draw it twice.
    /// </summary>
    public void Forget(long now)
    {
        _contacts.RemoveAll(c => c.Class != ContactClass.Object && c.Confidence(now, Profile) <= 0);

        int live = _contacts.Count(c => c.Class != ContactClass.Object);
        while (live > Profile.MaxContacts)
        {
            Contact? weakest = null;
            foreach (var c in _contacts)
            {
                if (c.Class != ContactClass.Object
                    && (weakest is null || c.Confidence(now, Profile) < weakest.Confidence(now, Profile)))
                    weakest = c;
            }
            _contacts.Remove(weakest!);
            live--;
        }
    }

    /// <summary>The contact for a key, creating an unknown one if it's new.</summary>
    internal Contact Touch(ContactKey key, long now)
    {
        if (Find(key) is { } existing)
            return existing;
        var contact = new Contact(key, now);
        _contacts.Add(contact);
        return contact;
    }

    /// <summary>
    /// A fresh cue: the estimate moves to where it came from (newest information wins), confidence
    /// rises to the cue's if that's higher than what's left, and the contact is no longer "searched".
    /// </summary>
    internal void Refresh(Contact contact, long now, TilePos estimate, int estimateZ, int confidence, int uncertainty, string cue)
    {
        contact.EstimateZ = estimateZ;
        int current = contact.Confidence(now, Profile);
        contact.ConfidenceAtUpdate = Math.Max(current, Math.Clamp(confidence, 0, 100));
        contact.UncertaintyAtUpdate = uncertainty;
        contact.Estimate = estimate;
        contact.LastUpdatedTick = now;
        contact.Searched = false;
        contact.Cue = cue;
    }

    /// <summary>Its jaws closed on this actor: certain where it is, and now it knows what it is.</summary>
    internal void Felt(Actor victim, TilePos at, int z, long now)
    {
        var contact = Touch(ContactKey.ForActor(victim.Id), now);
        if (contact.Class != ContactClass.Object)
            contact.Class = IsPrey(victim) ? ContactClass.Prey : ContactClass.Object;
        Refresh(contact, now, at, z, 100, uncertainty: 0, "bitten");
    }

    /// <summary>Looked where it was and found nothing: confidence drops to the searched cap.</summary>
    internal void MarkSearched(Contact contact, long now)
    {
        int current = contact.Confidence(now, Profile);
        contact.ConfidenceAtUpdate = Math.Min(current, Profile.SearchedConfidenceCap);
        contact.UncertaintyAtUpdate = contact.Uncertainty(now, Profile);
        contact.LastUpdatedTick = now;
        contact.Searched = true;
    }

    private bool IsPrey(Actor target) =>
        !target.IsLarge && target.Species.BodyHeightDm <= Profile.PreyMaxBodyHeightDm;

    private static int IndexOf(IReadOnlyList<LightSource> lights, LightSource light)
    {
        for (int i = 0; i < lights.Count; i++)
        {
            if (ReferenceEquals(lights[i], light))
                return i;
        }
        return -1;
    }

    private static long Distance2(TilePos a, TilePos b) =>
        (long)(a.X - b.X) * (a.X - b.X) + (long)(a.Y - b.Y) * (a.Y - b.Y);
}
