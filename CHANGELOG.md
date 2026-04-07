# Changelog

## Unreleased

### Added
- Added CLI pack scaffolding commands for creating and registering packs.
- Added CLI process scaffolding for quickly creating `Spine + LeafA + LeafB` process bundles.

### Changed
- Refactored `CommandData` so `Parameters` is the single runtime source of truth and `Parameter` is only a wrapper over the first item.
- Updated runtime strategies to use semantic output fields instead of `OutputConnections` during signal propagation.
- Updated comparer trigger backtracking to resolve source nodes from semantic output fields.

### Fixed
- Fixed command-node argument propagation so command execution reads the normalized parameter list.
- Fixed duplicate runtime propagation caused by mirrored `OutputConnections` and semantic edge fields being consumed together.
- Replaced `CanvasGroup.DOFade(...)` usage with `DOTween.To(...)` in `SpaceUIAnimator` to avoid asmdef/UI-module boundary issues.
- Removed unused box-drawing constants from `TUITool` to clear compiler warnings.
