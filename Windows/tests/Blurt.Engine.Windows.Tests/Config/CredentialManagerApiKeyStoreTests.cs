using Blurt.Engine.Config;
using Blurt.Engine.Windows.Config;
using FluentAssertions;

namespace Blurt.Engine.Windows.Tests.Config;

/// <summary>
/// The memo in front of credential storage, exercised against a fake <see cref="ICredentialAccess"/>
/// so read/write counts and an unreadable-storage response are assertable without touching the real
/// Credential Manager. Both halves of this suite guard a real regression: a locked/unreadable read
/// pinning "no key" for the process, and a write/memo disagreement under concurrent saves.
/// </summary>
public sealed class CredentialManagerApiKeyStoreTests
{
    [Test]
    public void AStoredKeyIsReadOnceAndServedFromTheMemoAfterThat()
    {
        var storage = new FakeCredentialAccess(stored: "sk-123");
        var store = new CredentialManagerApiKeyStore("target", storage);

        store.Current.Should().Be("sk-123");
        store.Current.Should().Be("sk-123");
        store.Current.Should().Be("sk-123");

        storage.Reads.Should().Be(1);
    }

    [Test]
    public void AnAbsentKeyIsMemoizedTooSoTheHotPathDoesNotReAsk()
    {
        var storage = new FakeCredentialAccess();
        var store = new CredentialManagerApiKeyStore("target", storage);

        store.Current.Should().BeNull();
        store.Current.Should().BeNull();

        storage.Reads.Should().Be(1);
    }

    [Test]
    public void AnUnreadableReadIsNeverMemoizedSoTheNextReadRetries()
    {
        var storage = new FakeCredentialAccess(stored: "sk-123");
        storage.ScriptReads([new CredentialReadResult.Unavailable(1312)]);
        var store = new CredentialManagerApiKeyStore("target", storage);

        store.Current.Should().BeNull();
        store.Current.Should().Be("sk-123");

        storage.Reads.Should().Be(2);
    }

    [Test]
    public void ARunOfUnreadableReadsKeepsRetryingRatherThanLatching()
    {
        var storage = new FakeCredentialAccess(stored: "sk-123");
        storage.ScriptReads(Enumerable.Repeat(new CredentialReadResult.Unavailable(1312), 3));
        var store = new CredentialManagerApiKeyStore("target", storage);

        store.Current.Should().BeNull();
        store.Current.Should().BeNull();
        store.Current.Should().BeNull();
        store.Current.Should().Be("sk-123");

        storage.Reads.Should().Be(4);
    }

    [Test]
    public void SaveWritesThenReadsBackSoTheMemoMatchesStorage()
    {
        var storage = new FakeCredentialAccess();
        var store = new CredentialManagerApiKeyStore("target", storage);

        var saved = store.Save("  sk-new  ");

        saved.Should().BeTrue();
        storage.Writes.Should().Be(1);
        store.Current.Should().Be("sk-new");
        storage.Stored.Should().Be("sk-new");
        // One read for the read-back; `Current` is served from the refreshed memo.
        storage.Reads.Should().Be(1);
    }

    [Test]
    public void SaveReplacesAMemoizedKeyRatherThanServingTheStaleOne()
    {
        var storage = new FakeCredentialAccess(stored: "sk-old");
        var store = new CredentialManagerApiKeyStore("target", storage);
        store.Current.Should().Be("sk-old");

        var saved = store.Save("sk-new");

        saved.Should().BeTrue();
        store.Current.Should().Be("sk-new");
    }

    [Test]
    public void ABlankSaveClearsTheKeyAndTheMemoTogether()
    {
        var storage = new FakeCredentialAccess(stored: "sk-123");
        var store = new CredentialManagerApiKeyStore("target", storage);
        store.Current.Should().Be("sk-123");

        var saved = store.Save("   \n");

        saved.Should().BeTrue();
        store.Current.Should().BeNull();
        storage.Stored.Should().BeNull();
    }

    [Test]
    public void AFailedWriteReportsFalseAndLeavesNoKeyMemoized()
    {
        var storage = new FakeCredentialAccess();
        storage.FailWrites();
        var store = new CredentialManagerApiKeyStore("target", storage);

        var saved = store.Save("sk-123");

        saved.Should().BeFalse();
        store.Current.Should().BeNull();
        storage.Stored.Should().BeNull();
    }

    [Test]
    public void AFailedWriteDoesNotClobberTheKeyAlreadyInStorage()
    {
        var storage = new FakeCredentialAccess(stored: "sk-good");
        storage.FailWrites();
        var store = new CredentialManagerApiKeyStore("target", storage);

        var saved = store.Save("sk-bad");

        saved.Should().BeFalse();
        store.Current.Should().Be("sk-good");
        storage.Stored.Should().Be("sk-good");
    }

    [Test]
    public void AnUnreadableReadBackLeavesTheMemoUnloadedNotWrong()
    {
        var storage = new FakeCredentialAccess();
        storage.ScriptReads([new CredentialReadResult.Unavailable(1312)]);
        var store = new CredentialManagerApiKeyStore("target", storage);

        var saved = store.Save("sk-123");

        saved.Should().BeTrue();
        storage.Stored.Should().Be("sk-123");
        store.Current.Should().Be("sk-123");
        storage.Reads.Should().Be(2);
    }

    [Test]
    public async Task ConcurrentSavesLeaveTheMemoAgreeingWithStorage()
    {
        var storage = new FakeCredentialAccess();
        var store = new CredentialManagerApiKeyStore("target", storage);

        var saves = Enumerable.Range(0, 50)
            .Select(i => Task.Run(() => store.Save(i % 2 == 0 ? "sk-even" : "sk-odd")));
        await Task.WhenAll(saves);

        store.Current.Should().Be(storage.Stored);
        store.Current.Should().NotBeNull();
    }

    [Test]
    public void SaveTrimsSurroundingWhitespaceBeforeWriting()
    {
        var storage = new FakeCredentialAccess();
        var store = new CredentialManagerApiKeyStore("target", storage);

        store.Save("  sk-trim  \n");

        store.Current.Should().Be("sk-trim");
        storage.Stored.Should().Be("sk-trim");
    }

    [Test]
    public void HasKeyReflectsWhetherAKeyIsStored()
    {
        IApiKeyGateway store = new CredentialManagerApiKeyStore("target", new FakeCredentialAccess());
        store.HasKey.Should().BeFalse();

        store.Save("sk-abc123");

        store.HasKey.Should().BeTrue();
    }

    [Test]
    public void TheSingleArgumentConstructorWiresToTheRealCredentialStoreIsolatedFromBlurtAndBlurtDev()
    {
        var target = $"blurt-test-{Guid.NewGuid():N}";
        try
        {
            var store = new CredentialManagerApiKeyStore(target);

            store.Save("sk-from-credential-manager");

            store.Current.Should().Be("sk-from-credential-manager");
        }
        finally
        {
            new CredentialManagerApiKeyStore(target).Save(string.Empty);
        }
    }

    /// <summary>
    /// One slot, mirroring <see cref="CredentialManagerCredentialAccess"/>'s Read/Write/Delete
    /// contract, plus a scripted read queue so the unreadable-storage path is exercisable. Counts
    /// reads and writes, which is how the memo's whole purpose — not consulting storage twice —
    /// becomes observable.
    /// </summary>
    private sealed class FakeCredentialAccess(string? stored = null) : ICredentialAccess
    {
        private readonly Lock _gate = new();
        private readonly Queue<CredentialReadResult> _scriptedReads = new();
        private string? _stored = stored;
        private bool _writesSucceed = true;

        public int Reads { get; private set; }

        public int Writes { get; private set; }

        public string? Stored
        {
            get
            {
                lock (_gate)
                {
                    return _stored;
                }
            }
        }

        public void ScriptReads(IEnumerable<CredentialReadResult> outcomes)
        {
            lock (_gate)
            {
                foreach (var outcome in outcomes)
                {
                    _scriptedReads.Enqueue(outcome);
                }
            }
        }

        public void FailWrites()
        {
            lock (_gate)
            {
                _writesSucceed = false;
            }
        }

        public CredentialReadResult Read(string target)
        {
            lock (_gate)
            {
                Reads++;
                if (_scriptedReads.Count > 0)
                {
                    return _scriptedReads.Dequeue();
                }

                return _stored is { } value ? new CredentialReadResult.Found(value) : CredentialReadResult.Absent;
            }
        }

        public bool Write(string target, string value)
        {
            lock (_gate)
            {
                Writes++;
                if (!_writesSucceed)
                {
                    return false;
                }

                _stored = value;
                return true;
            }
        }

        public bool Delete(string target)
        {
            lock (_gate)
            {
                Writes++;
                if (!_writesSucceed)
                {
                    return false;
                }

                _stored = null;
                return true;
            }
        }
    }
}
