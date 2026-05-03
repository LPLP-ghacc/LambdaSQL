namespace LambdaSQL.Core.Storage.PagedStorage;

/// <summary>
/// Manages the on-disk page file for a single table.
/// Each file = sequence of 8KB pages.
/// Uses a simple free-page list for reuse after deletes.
/// </summary>
public sealed class TableFile : IDisposable
{
    private readonly FileStream _file;
    private readonly List<Page> _pageCache = new();
    private readonly Queue<int> _freePages = new();

    public TableFile(string path)
    {
        _file = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
            FileShare.None, bufferSize: Page.PageSize * 4, FileOptions.RandomAccess);
        LoadPages();
    }

    // ── Load all pages from disk ──────────────────────────────────────────────

    private void LoadPages()
    {
        _file.Seek(0, SeekOrigin.Begin);
        var buf = new byte[Page.PageSize];
        int pageId = 0;

        while (_file.Read(buf) == Page.PageSize)
        {
            var copy = new byte[Page.PageSize];
            buf.CopyTo(copy, 0);
            _pageCache.Add(new Page(pageId, copy));
            pageId++;
        }
    }

    // ── Write a row, returns (pageId, slotId) ────────────────────────────────

    public (int pageId, int slotId) WriteRow(byte[] rowData)
    {
        // Try existing pages with free space
        foreach (var page in _pageCache)
        {
            int slot = page.WriteRow(rowData);
            if (slot >= 0)
            {
                FlushPage(page);
                return (page.PageId, slot);
            }
        }

        // Allocate new page
        var newPage = new Page(_pageCache.Count);
        int newSlot = newPage.WriteRow(rowData);
        _pageCache.Add(newPage);
        FlushPage(newPage);
        return (newPage.PageId, newSlot);
    }

    // ── Read a row ────────────────────────────────────────────────────────────

    public ReadOnlySpan<byte> ReadRow(int pageId, int slotId)
    {
        if (pageId >= _pageCache.Count) return ReadOnlySpan<byte>.Empty;
        return _pageCache[pageId].ReadRow(slotId);
    }

    // ── Delete a row ──────────────────────────────────────────────────────────

    public void DeleteRow(int pageId, int slotId)
    {
        if (pageId >= _pageCache.Count) return;
        _pageCache[pageId].DeleteRow(slotId);
        FlushPage(_pageCache[pageId]);
    }

    // ── Scan all rows ─────────────────────────────────────────────────────────

    public IEnumerable<(int pageId, int slotId, ReadOnlyMemory<byte> data)> ScanAll()
    {
        foreach (var page in _pageCache)
        {
            for (int s = 0; s < page.SlotCount; s++)
            {
                var span = page.ReadRow(s);
                if (span.IsEmpty) continue; // deleted
                yield return (page.PageId, s, span.ToArray());
            }
        }
    }

    // ── Flush a page to disk ──────────────────────────────────────────────────

    private void FlushPage(Page page)
    {
        long offset = (long)page.PageId * Page.PageSize;
        _file.Seek(offset, SeekOrigin.Begin);
        _file.Write(page.AsSpan());
    }

    public void Flush() => _file.Flush(flushToDisk: false);

    public void Dispose() => _file.Dispose();
}
