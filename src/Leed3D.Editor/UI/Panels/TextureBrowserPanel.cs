using System.Numerics;
using ImGuiNET;
using Leed3D.Editor.Texturing;
using Leed3D.Kernel.Geometry;
using Leed3D.Kernel.Scene;
using Leed3D.Kernel.Selection;
using Leed3D.Kernel.Texturing;
using Raylib_cs;

namespace Leed3D.Editor.UI.Panels;

/// <summary>
/// ImGui panel displaying a grid of 64x64 texture thumbnails for all registered materials,
/// category/search filtering, folder scan trigger, and one-click assignment to selected objects/faces.
/// </summary>
public sealed class TextureBrowserPanel
{
    private string _filterSearch = string.Empty;

    public void Draw(EditorApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        ImGui.Begin("Texture Browser");

        // Filter / Search input
        ImGui.SetNextItemWidth(180.0f);
        ImGui.InputTextWithHint("##TextureSearch", "Search textures...", ref _filterSearch, 64);
        ImGui.SameLine();
        if (ImGui.Button("Clear##ClearSearch"))
        {
            _filterSearch = string.Empty;
        }

        ImGui.SameLine();
        if (ImGui.Button("Scan Textures Folder"))
        {
            string dir = Path.Combine(Directory.GetCurrentDirectory(), "textures");
            if (Directory.Exists(dir))
            {
                int count = app.MaterialLibrary.ScanDirectory(dir);
                app.StatusMessage = $"Scanned textures: registered {count} file materials.";
            }
            else
            {
                app.StatusMessage = $"Textures directory not found: {dir}";
            }
        }

        ImGui.Separator();

        ImGui.TextDisabled($"Active Material: {app.ActiveMaterialId}");
        ImGui.Separator();

        // Display thumbnail grid
        float panelWidth = ImGui.GetContentRegionAvail().X;
        float itemSize = 64.0f;
        float padding = 8.0f;
        int columns = Math.Max(1, (int)((panelWidth + padding) / (itemSize + padding)));

        if (ImGui.BeginTable("TextureGrid", columns))
        {
            int col = 0;
            foreach (MaterialEntry mat in app.MaterialLibrary.Materials)
            {
                if (!string.IsNullOrWhiteSpace(_filterSearch) &&
                    !mat.Id.Contains(_filterSearch, StringComparison.OrdinalIgnoreCase) &&
                    !mat.DisplayName.Contains(_filterSearch, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (col == 0)
                {
                    ImGui.TableNextRow();
                }

                ImGui.TableSetColumnIndex(col);
                col = (col + 1) % columns;

                ImGui.PushID(mat.Id);

                Texture2D gpuTex = app.TextureCache.GetTexture(mat.Id);
                bool isSelected = string.Equals(app.ActiveMaterialId, mat.Id, StringComparison.OrdinalIgnoreCase);

                if (isSelected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1.0f, 0.65f, 0.0f, 1.0f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2.0f);
                }

                nint texHandle = (nint)gpuTex.Id;
                if (ImGui.ImageButton($"btn_{mat.Id}", texHandle, new Vector2(itemSize, itemSize)))
                {
                    app.ActiveMaterialId = mat.Id;
                    app.ApplyMaterialToSelection(mat.Id);
                }

                if (isSelected)
                {
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor();
                }

                if (ImGui.IsItemHovered())
                {
                    ImGui.BeginTooltip();
                    ImGui.Text($"{mat.DisplayName} ({mat.Id})");
                    ImGui.TextDisabled($"Source: {mat.Source}");
                    ImGui.TextDisabled("Click to assign to selection");
                    ImGui.EndTooltip();
                }

                string shortName = mat.DisplayName.Length > 9 ? mat.DisplayName[..8] + ".." : mat.DisplayName;
                ImGui.Text(shortName);

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        ImGui.End();
    }
}
