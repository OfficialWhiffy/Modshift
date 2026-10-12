using TruckModsListUpdater.Core.Archives;

namespace TruckModsListUpdater.Core.Tests.Archives;

public class CityHashTests
{
    // Ground truth: hashes found in the entry table of a real HashFS v1 mod archive. They cover
    // every code path of CityHash64 v1.0.3 (0, 1-3, 4-8, 9-16, 17-32, 33-64 and >64 bytes).
    [Theory]
    [InlineData("", 0x9ae16a3b2f90404fUL)]
    [InlineData("def", 0x2c6f469efb31c45aUL)]
    [InlineData("icon.jpg", 0x5816af2ddac5a9f6UL)]
    [InlineData("document.txt", 0x1600b386d44fbf57UL)]
    [InlineData("def/city.roext.sii", 0xa91154f5a9ffa046UL)]
    [InlineData("def/world/building_model.blue.sii", 0x55546bea1e05afd5UL)]
    [InlineData("def/vehicle/trailer/scs_box/reefer/company_paint_job/ccarrion.sii", 0x0cf4090b8892cb0aUL)]
    [InlineData("def/vehicle/trailer/willig/fuel_cistern/company_paint_job/petro.sii", 0x088c5bf80c6861b5UL)]
    public void MatchesHashesUsedByScsArchives(string path, ulong expected)
    {
        CityHash.CityHash64(path).Should().Be(expected);
    }

    [Fact]
    public void LongInputs_SpanningSeveralChunks_AreStable()
    {
        // Guards the 64-byte chunk loop against index mistakes (no exceptions, deterministic).
        var data = Enumerable.Range(0, 1000).Select(i => (byte)i).ToArray();
        for (int len = 65; len <= data.Length; len += 37)
            CityHash.CityHash64(data.AsSpan(0, len)).Should().Be(CityHash.CityHash64(data.AsSpan(0, len).ToArray()));
    }
}
