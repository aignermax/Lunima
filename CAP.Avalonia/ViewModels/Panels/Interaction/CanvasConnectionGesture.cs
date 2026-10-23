using CAP.Avalonia.Commands;
using CAP.Avalonia.ViewModels.Canvas;
using CAP_Core.Components.Core;
using CAP_Core.Components.PinKinds;

namespace CAP.Avalonia.ViewModels.Panels.Interaction;

/// <summary>Owns the pending first pin and the complete connect gesture.</summary>
internal sealed class CanvasConnectionGesture
{
    private readonly DesignCanvasViewModel _canvas;
    private readonly CommandManager _commandManager;
    private readonly Action<string> _status;
    private PhysicalPin? _connectionStartPin;

    public CanvasConnectionGesture(DesignCanvasViewModel canvas, CommandManager commands, Action<string> status)
    {
        _canvas = canvas;
        _commandManager = commands;
        _status = status;
    }

    public void Reset() => _connectionStartPin = null;

    /// <summary>
    /// Handles mouse movement on canvas (for pin highlighting in Connect mode).
    /// </summary>
    public void MouseMove(bool connectMode, double canvasX, double canvasY)
    {
        if (connectMode)
        {
            var nearPin = _canvas.UpdatePinHighlight(canvasX, canvasY, _connectionStartPin);

            if (nearPin != null)
            {
                var pinName = nearPin.Name;
                var compName = nearPin.ParentComponentViewModel.Name;

                if (_connectionStartPin != null)
                {
                    _status($"Click to connect to {pinName} on {compName}");
                }
                else
                {
                    _status($"Click {pinName} on {compName} to start connection");
                }
            }
            else if (_connectionStartPin != null)
            {
                _status($"Connection started from {_connectionStartPin.Name}. Move near a pin to connect.");
            }
            else
            {
                _status("Connect mode: Move near a pin to start connection");
            }
        }
        else
        {
            _canvas.ClearPinHighlight();
        }
    }

    public void PinClicked(PhysicalPin pin)
    {
        if (_connectionStartPin == null)
        {
            _connectionStartPin = pin;
            _status($"Connection started from {pin.Name}. Click another pin to complete.");
        }
        else
        {
            if (_connectionStartPin == pin)
            {
                // A different pin of the same component IS allowed (feedback
                // loops, ring self-coupling, black-box GDS imports).
                _status("Cannot connect pin to itself");
            }
            else if (!PinKindHelper.ArePlanarRoutingCompatible(_connectionStartPin, pin))
            {
                // Cross-domain connection (optical ↔ electrical) is physically meaningless — reject.
                _status(PinKindHelper.DescribeIncompatibility(_connectionStartPin, pin));
            }
            else if (CAP_Core.Components.Dies.DieWiringPolicy.Evaluate(_connectionStartPin, pin).Verdict
                     != CAP_Core.Components.Dies.DieWiringVerdict.Waveguide)
            {
                // Across a die edge only facing edge couplers connect — as a die-to-die link.
                var wire = Commands.Dies.FacetWireCommandFactory.Create(_canvas, _connectionStartPin, pin);
                if (wire.Command != null)
                    _commandManager.ExecuteCommand(wire.Command);
                else
                    _status(Services.Localization.LocalizationService.Instance.Translate(
                        wire.RefusalKey ?? "Dies.Wiring.CouplersOnly"));
            }
            else
            {
                var cmd = new CreateConnectionCommand(_canvas, _connectionStartPin, pin);
                _commandManager.ExecuteCommand(cmd);
                _status($"Connected {_connectionStartPin.Name} to {pin.Name}");
            }
            _connectionStartPin = null;
        }
    }

}
