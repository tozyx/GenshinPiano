# GenshinPiano v3

English | [简体中文](README.md)

GenshinPiano v3 is a Windows application for arranging and performing music with instruments in Genshin Impact. It is being completely rebuilt with C#, .NET 10 LTS, and WPF.

## Goals

- Use UTF-8 JSON `.gpiano` files as the editable project format
- Import and export standard MIDI files
- Support importing legacy `.GenshinPiano` scores
- Provide a piano roll, a 21-key preview, transposition, and pitch-range mapping
- Provide reliable Windows keyboard playback and recording
- Provide numbered-notation OCR and printed staff-notation OMR through an optional add-on

## Solution Structure

- `src/GenshinPiano.Core`: score domain models and pure business rules
- `src/GenshinPiano.Application`: use cases, ports, and application services
- `src/GenshinPiano.Infrastructure`: JSON, MIDI, legacy-format, and Windows platform adapters
- `src/GenshinPiano.App`: WPF desktop application
- `tests/GenshinPiano.Core.Tests`: core model tests
- `docs`: format and architecture documentation

## Development Environment

- Windows 10/11 x64
- .NET 10 SDK
- Visual Studio 2026 with the **.NET desktop development** workload, or another IDE with WPF support

## How to build

For development setup, debugging, tests, Release publishing, update signing, and OCR add-on packaging, see:

[Development and build guide](docs/BUILDING.md#english)

## User guide

For application controls, piano-roll shortcuts, MIDI/OCR import, local audition, and in-game playback, see:

[GenshinPiano user guide](docs/USER_GUIDE.md#english)

## Portable Configuration

The application runs in portable mode. User settings are stored in `config/settings.json` beside the executable. When distributing a ZIP package, extract the entire package to a directory where the user has write permission. Installing it directly under `Program Files` is not recommended.

## Current Features

- Edit, validate, and save UTF-8 JSON `.gpiano` scores, with a folder library, drag-and-drop opening, renaming, and unsaved-work recovery
- 21-key, full 88-key, and score-range piano-roll views with note creation/audition, marquee and additive selection, copying, grouped movement, rhythmic length, and key-hold editing; view mode and zoom are persisted
- Multi-instrument local audition with BPM, natural sustain, playback cursor, selection looping, and smooth high-refresh-rate scrolling
- Safe in-game keyboard performance with target-window detection, a three-second countdown, focus-loss pause, global Esc pause, and guaranteed key release
- Direct and batch MIDI import plus legacy `.GenshinPiano` conversion
- Score analysis, 21-key range adjustment, intelligent hold-duration optimization, and short-press generation
- Experimental score OCR with an explicit numbered/staff selector: numbered notation supports watermark suppression, row/voice analysis, rhythm and tie reconstruction, while the Oemer/MusicXML staff pipeline preserves polyphony, accidentals, and durations; results can remain on the 88-key roll or be mapped to 21 keys
- Dark/light themes, Chinese/English UI, portable settings, `.gpiano` file association, and single-instance operation
- GitHub/GitCode update racing, resumable downloads, RSA signature verification, seamless updates, release notes, and manual rollback

The legacy format does not store BPM. Batch conversion currently imports files at 120 BPM and 480 PPQ, preserves legacy values as rhythmic spans, and generates an 80% key-hold duration using the Natural articulation rule. Existing `.gpiano` files with the same name in the output directory are skipped by default.

## Score Resource Notice
The score files in this directory are provided to demonstrate and test GenshinPiano features, including score editing, file loading, local audition, format conversion, and in-game performance.

Unless a score file or its accompanying information explicitly states otherwise:

- The scores are intended only for personal study, software testing, and non-commercial exchange.
- The scores may not be sold, commercially distributed, used in paid performances, or otherwise commercially exploited.
- A score may be a simplified transcription or arrangement of an existing musical work.
- Copyright and other rights in the underlying musical works remain with their respective composers, authors, publishers, and other rights holders.
- Distribution of a score does not mean that the GenshinPiano project owns or has obtained full authorization for the underlying musical work.
- Distribution of a score does not grant users rights to reproduce, adapt, distribute, publicly perform, or commercially exploit the underlying work.
- Users are responsible for ensuring that their downloading, use, modification, and sharing of score files comply with applicable laws and platform rules.

The GenshinPiano source code is released under the MIT License. The MIT License applies only to software code and original materials that the project is legally entitled to license. It does not automatically apply to third-party musical works, score files, titles, or other materials in this directory.

If you are a copyright owner or an authorized representative and believe that content in this directory infringes your rights, please contact the project through GitHub Issues:

[https://github.com/tozyx/GenshinPiano/issues](https://github.com/tozyx/GenshinPiano/issues)

This project is licensed under the [MIT License](LICENSE).
