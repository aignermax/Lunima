# Visual learning journey

The novice builds a small circuit with the real tools. Guidance demonstrates the next action beside its actual target rather than describing it in a remote text box. A repeating ghost pointer approaches the target and shows a click pulse; it never performs input. One short localized phrase accompanies it. Real user actions advance the journey; completion gets a brief positive acknowledgement.

Start with choosing and placing two compatible optical components, then show connection mode and the actual pins, then simulation. A die alone does not count as a component. Never suggest connecting after only one component. Do not claim success from a Next button. Wrong actions, undo, existing projects and unavailable targets must not strand the user or fabricate progress.

Guidance is nonmodal and dismissible. Preserve ordinary editing and unsaved work. Offer a clear way back to guidance. First-use guidance is enabled unless the user declines; remember that choice. Details remain optional. Existing logic and system journeys should gain the same visual demonstration without breaking their action predicates.

The overlay must track resize and target movement, respect reduced motion, pass pointer input through decorations, and stop timers/animations while hidden or detached. Avoid repeatedly scanning the entire visual tree for a known target. Missing targets must not point at unrelated controls.

Python setup is a separate background subsystem. Learning, placing and connecting must remain available while setup runs or fails. No new Python dependency for guidance. English and German screenshots plus actual UI-flow tests establish the novice experience; all supported locales get meaningful copy.

Release only after independent review, targeted tests, complete CI, and testing the integrated desktop build. A polished appearance alone is not evidence that the journey works.
