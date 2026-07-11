/*

  This Source Code Form is subject to the terms of the Mozilla Public
  License, v. 2.0. If a copy of the MPL was not distributed with this
  file, You can obtain one at http://mozilla.org/MPL/2.0/.

  Copyright (C) 2009-2020 Michael Möller <mmoeller@openhardwaremonitor.org>

*/

using System;

// ponytail: was CLSCompliant(true) — spurious CLS warnings on uint params, SymbolRegular base types.
// This WPF desktop app has no external CLS-compliant consumers. Disable to remove ~22 warnings.
[assembly: CLSCompliant(false)]
