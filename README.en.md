# GenshinPiano v3

English | [简体中文](README.md)

GenshinPiano v3 is a Windows application for arranging and performing music with instruments in Genshin Impact. It is being completely rebuilt with C#, .NET 10 LTS, and WPF.

## Goals

- Use UTF-8 JSON `.gpiano` files as the editable project format
- Import and export standard MIDI files
- Support importing legacy `.GenshinPiano` scores
- Provide a piano roll, a 21-key preview, transposition, and pitch-range mapping
- Provide reliable Windows keyboard playback and recording
- Support keyboard-score OCR, printed staff-notation OMR, and numbered-notation OMR in the future

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

## Portable Configuration

The application runs in portable mode. User settings are stored in `config/settings.json` beside the executable. When distributing a ZIP package, extract the entire package to a directory where the user has write permission. Installing it directly under `Program Files` is not recommended.

## Current Features

- Open, validate, and save UTF-8 JSON `.gpiano` scores
- Map score timelines and tempo changes to the 21 Genshin Impact keys, then perform them through the Windows `SendInput` API
- After playback is requested, wait for the Genshin Impact window to receive focus and then run a three-second safety countdown; losing focus during the countdown resets it and returns to the waiting state
- Play, pause, and resume from the sidebar; an animated stop button appears while playback is active
- Send keystrokes only while the Chinese client (`YuanShen.exe`) or global client (`GenshinImpact.exe`) is in the foreground; losing focus freezes the timeline, and playback resumes after focus returns
- Listen globally for Esc during playback and the countdown; when Genshin Impact is in the foreground, Esc releases held keys and pauses playback while still being passed through to the game
- Recursively convert legacy `.GenshinPiano` files from **Import → Batch Convert Legacy Scores**, preserving their directory structure
- Edit scores with the built-in 21-key piano roll: Ctrl-click selection, marquee selection, Ctrl-marquee additive selection, grouped movement, copying, and deletion are supported. Copying keeps the original notes visible and shows a translucent destination preview. Right-click a note to adjust its duration, or use `[` and `]` to change the selected notes or the current snap grid

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
