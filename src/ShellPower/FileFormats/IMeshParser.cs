﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace SSCP.ShellPower {
    public interface IMeshParser {
        void Parse(String filename);
        Mesh GetMesh();
    }
}
