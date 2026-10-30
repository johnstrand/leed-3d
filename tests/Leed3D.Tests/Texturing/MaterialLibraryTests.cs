using Leed3D.Kernel.Texturing;

namespace Leed3D.Tests.Texturing;

public sealed class MaterialLibraryTests
{
    [Fact]
    public void BuiltInMaterialIds_ContainsAll12SpecifiedMaterials()
    {
        string[] expected =
        [
            "dev_grid_gray",
            "dev_grid_orange",
            "dev_grid_blue",
            "dev_grid_green",
            "dev_grid_purple",
            "dev_grid_red",
            "dev_uv_checker",
            "dev_trigger",
            "dev_portal",
            "dev_clip",
            "dev_lava",
            "dev_water"
        ];

        Assert.Equal(12, ProceduralTextureGenerator.BuiltInMaterialIds.Length);
        foreach (string id in expected)
        {
            Assert.Contains(id, ProceduralTextureGenerator.BuiltInMaterialIds);
        }
    }

    [Theory]
    [InlineData("dev_grid_gray")]
    [InlineData("dev_grid_orange")]
    [InlineData("dev_grid_blue")]
    [InlineData("dev_grid_green")]
    [InlineData("dev_grid_purple")]
    [InlineData("dev_grid_red")]
    [InlineData("dev_uv_checker")]
    [InlineData("dev_trigger")]
    [InlineData("dev_portal")]
    [InlineData("dev_clip")]
    [InlineData("dev_lava")]
    [InlineData("dev_water")]
    public void ProceduralTextureGenerator_GeneratesValid256x256RgbaBuffer(string materialId)
    {
        RawTextureData texture = ProceduralTextureGenerator.Generate(materialId, 256, 256);

        Assert.Equal(materialId, texture.Id);
        Assert.Equal(256, texture.Width);
        Assert.Equal(256, texture.Height);
        Assert.Equal(256 * 256 * 4, texture.RgbaPixels.Length);

        // Verify buffer is not all zeros
        bool hasNonZero = false;
        for (int i = 0; i < texture.RgbaPixels.Length; i++)
        {
            if (texture.RgbaPixels[i] != 0)
            {
                hasNonZero = true;
                break;
            }
        }
        Assert.True(hasNonZero);
    }

    [Fact]
    public void MaterialLibrary_RegistersBuiltInMaterialsAndProvidesCachedData()
    {
        var lib = new MaterialLibrary();

        Assert.True(lib.Materials.Count >= 12);
        foreach (string id in ProceduralTextureGenerator.BuiltInMaterialIds)
        {
            Assert.True(lib.HasMaterial(id));
            MaterialEntry? entry = lib.GetMaterial(id);
            Assert.NotNull(entry);
            Assert.Equal(MaterialSource.Procedural, entry.Source);

            RawTextureData data = lib.GetTextureData(id);
            Assert.NotNull(data);
            Assert.Equal(256, data.Width);
            Assert.Equal(256, data.Height);
        }
    }

    [Fact]
    public void MaterialLibrary_ScanDirectory_RecursivelyDiscoversAndRegistersImageFiles()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "Leed3D_MatLibTest_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            string subDir = Path.Combine(tempDir, "stone", "brick");
            Directory.CreateDirectory(subDir);

            // Create fake .png and .jpg files
            string file1 = Path.Combine(tempDir, "metal_plate.png");
            string file2 = Path.Combine(subDir, "rough_wall.jpg");
            string ignoreFile = Path.Combine(tempDir, "notes.txt");

            File.WriteAllBytes(file1, [1, 2, 3, 4]);
            File.WriteAllBytes(file2, [5, 6, 7, 8]);
            File.WriteAllText(ignoreFile, "hello world");

            var lib = new MaterialLibrary();
            int count = lib.ScanDirectory(tempDir);

            Assert.Equal(2, count);
            Assert.True(lib.HasMaterial("metal_plate"));
            Assert.True(lib.HasMaterial("stone/brick/rough_wall"));

            MaterialEntry? entry1 = lib.GetMaterial("metal_plate");
            Assert.NotNull(entry1);
            Assert.Equal(MaterialSource.File, entry1.Source);
            Assert.Equal(file1, entry1.FilePath);

            MaterialEntry? entry2 = lib.GetMaterial("stone/brick/rough_wall");
            Assert.NotNull(entry2);
            Assert.Equal(MaterialSource.File, entry2.Source);
            Assert.Equal(file2, entry2.FilePath);
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                Directory.Delete(tempDir, recursive: true);
            }
        }
    }
}
