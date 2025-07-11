# Changelog

All notable changes to this repository will be documented in this file.

The format is based on [Keep a Changelog](http://keepachangelog.com/en/1.0.0/) and this project adheres to [Semantic Versioning](http://semver.org/spec/v2.0.0.html).


## Unreleased

Clear static data on destroy

### Upgrade Notes

### Known Issues

### Added

### Changed

### Deprecated

### Removed

### Fixed

## [0.7.2-preview] - 2022-07-11

Added a Visualization Layer property to DrawingManager3d to allow developers to specify which layer the visualizations render into.
   Defaults to UI layer, and the sensors in sensors package will set the culling mask to not sense the UI layer. If you add a new layer
   and choose that (or another builtin layer), you should deselect that layer in the culling mask of any sensors that have a camera component.

## [0.7.0-preview] - 2022-02-01

### Upgrade Notes

Making the package version consistent with the ROS-TCP-Connector release tag to avoid confusion.

### Added

Visualizations work with ROS services


## [0.1.0-preview] - 2021-09-30

Visualization package 0.1.0 preview is public.
