using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Numerics;
using WorldBuilder.Shared.Modules.Landscape.Models;
using WorldBuilder.Shared.Models;

namespace WorldBuilder.Shared.Modules.Landscape.Tools
{
    public class BrushTool : ObservableObject, ILandscapeTool
    {
        private const int TextureId = 5;

        public string Name => "Brush";
        public string IconGlyph => "🖌️";
        public bool IsActive { get; private set; }

        private LandscapeToolContext? _context;
        private bool _isPainting;
        private TerrainRaycast.TerrainRaycastHit _lastHit;
        private CompoundCommand? _currentStroke;

        private float _brushRadius = 5f;
        public float BrushRadius
        {
            get => _brushRadius;
            set => SetProperty(ref _brushRadius, value);
        }

        private float _brushStrength = 1f;
        public float BrushStrength
        {
            get => _brushStrength;
            set => SetProperty(ref _brushStrength, value);
        }

        public void Activate(LandscapeToolContext context)
        {
            _context = context;
            IsActive = true;
        }

        public void Deactivate()
        {
            IsActive = false;
            _context = null;
        }

        public void Update(double deltaTime)
        {
        }

        public bool OnPointerPressed(ViewportInputEvent e)
        {
            if (_context == null || !e.IsLeftDown)
            {
                _context?.Logger.LogWarning("BrushTool.OnPointerPressed ignored. Context: {Context}, IsLeft: {Left}", _context == null ? "null" : "valid", e.IsLeftDown);
                return false;
            }

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit)
            {
                _isPainting = true;
                _lastHit = hit;
                _currentStroke = new CompoundCommand("Brush Stroke");
                ApplyPaint(hit);
                return true;
            }
            else
            {
                _context.Logger.LogWarning("BrushTool Raycast Missed. Pos: {Pos}", e.Position);
            }

            return false;
        }

        public bool OnPointerMoved(ViewportInputEvent e)
        {
            if (!_isPainting || _context == null) return false;

            var hit = Raycast(e.Position.X, e.Position.Y);
            if (hit.Hit)
            {
                ApplyPaint(hit);
                _lastHit = hit;
                return true;
            }

            return false;
        }

        public bool OnPointerReleased(ViewportInputEvent e)
        {
            if (_isPainting)
            {
                _isPainting = false;
                if (_currentStroke != null)
                {
                    _context?.CommandHistory.Execute(_currentStroke);
                    _currentStroke = null;
                }
                return true;
            }
            return false;
        }

        private TerrainRaycast.TerrainRaycastHit Raycast(double x, double y)
        {
            if (_context == null || _context.Document.Region == null) return new TerrainRaycast.TerrainRaycastHit();

            // Use ViewportSize from context
            return TerrainRaycast.Raycast((float)x, (float)y, (int)_context.ViewportSize.X, (int)_context.ViewportSize.Y, _context.Camera, _context.Document.Region, _context.Document.TerrainCache);
        }

        private void ApplyPaint(TerrainRaycast.TerrainRaycastHit hit)
        {
            if (_context == null || _currentStroke == null) return;
            var command = new PaintCommand(_context, hit.HitPosition, BrushRadius, TextureId);
            _currentStroke.Add(command);
            command.Execute();
        }
    }
}
