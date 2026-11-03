#!/usr/bin/env python3
"""netmeta — list public API surface of .NET assemblies from metadata (no decompiler, no .NET runtime).

Answers "does member X exist, what is its signature, is it public/static" — not method bodies.
For bodies, use ResoniteDumper / ILSpy.

Requires: pip install dnfile

Usage:
  python netmeta.py <dll-or-dir> type <Name|Namespace.Name>   # full member listing of matching types
  python netmeta.py <dll-or-dir> find <regex>                 # search type and member names
  options: --all  include private/internal members (default: public + protected)
"""
import os
import re
import struct
import sys

import dnfile

PRIM = {0x01: "void", 0x02: "bool", 0x03: "char", 0x04: "sbyte", 0x05: "byte", 0x06: "short",
        0x07: "ushort", 0x08: "int", 0x09: "uint", 0x0A: "long", 0x0B: "ulong", 0x0C: "float",
        0x0D: "double", 0x0E: "string", 0x16: "TypedReference", 0x18: "nint", 0x19: "nuint",
        0x1C: "object"}


class Asm:
    def __init__(self, path):
        self.path = path
        self.name = os.path.basename(path)
        self.pe = dnfile.dnPE(path)
        self.t = self.pe.net.mdtables
        self.nest = {}
        if self.t.NestedClass:
            for r in self.t.NestedClass:
                self.nest[r.NestedClass.row_index] = r.EnclosingClass.row_index
        self.gparams = {}
        if self.t.GenericParam:
            for r in self.t.GenericParam:
                key = (r.Owner.table.name if r.Owner.table else "?", r.Owner.row_index)
                self.gparams.setdefault(key, {})[r.Number] = str(r.Name)
        self.consts = {}
        if self.t.Constant:
            for r in self.t.Constant:
                if r.Parent.table and r.Parent.table.name == "Field":
                    self.consts[r.Parent.row_index] = (r.Type, r.Value.value if r.Value else b"")
        self.props = {}
        if self.t.PropertyMap:
            for r in self.t.PropertyMap:
                self.props[r.Parent.row_index] = [i.row_index for i in r.PropertyList]
        self.events = {}
        if self.t.EventMap:
            for r in self.t.EventMap:
                self.events[r.Parent.row_index] = [i.row_index for i in r.EventList]
        self.ifaces = {}
        if self.t.InterfaceImpl:
            for r in self.t.InterfaceImpl:
                self.ifaces.setdefault(r.Class.row_index, []).append(r.Interface)

    # ---- names
    def typedef_name(self, idx, full=True):
        r = self.t.TypeDef[idx - 1]
        name = str(r.TypeName)
        if idx in self.nest:
            return self.typedef_name(self.nest[idx], full) + "+" + name
        ns = str(r.TypeNamespace)
        return f"{ns}.{name}" if (full and ns) else name

    def typeref_name(self, idx, full=False):
        r = self.t.TypeRef[idx - 1]
        name = str(r.TypeName)
        ns = str(r.TypeNamespace)
        return f"{ns}.{name}" if (full and ns) else name

    def tdor(self, coded, full=False):
        if coded is None or coded.table is None:
            return "?"
        tn = coded.table.name
        if tn == "TypeDef":
            return self.typedef_name(coded.row_index, full)
        if tn == "TypeRef":
            return self.typeref_name(coded.row_index, full)
        if tn == "TypeSpec":
            sig = self.t.TypeSpec[coded.row_index - 1].Signature.value
            return Sig(self, sig, None).type()
        return "?"


class Sig:
    def __init__(self, asm, data, owner):
        self.a, self.d, self.p, self.owner = asm, data, 0, owner  # owner: (typeIdx, methodIdx)

    def u8(self):
        b = self.d[self.p]
        self.p += 1
        return b

    def cu(self):
        b = self.u8()
        if b & 0x80 == 0:
            return b
        if b & 0xC0 == 0x80:
            return ((b & 0x3F) << 8) | self.u8()
        return ((b & 0x1F) << 24) | (self.u8() << 16) | (self.u8() << 8) | self.u8()

    def tok(self):
        v = self.cu()
        tag, idx = v & 3, v >> 2
        if tag == 0:
            return self.a.typedef_name(idx, False)
        if tag == 1:
            return self.a.typeref_name(idx)
        return Sig(self.a, self.a.t.TypeSpec[idx - 1].Signature.value, self.owner).type()

    def gname(self, kind, n):
        if self.owner:
            key = ("TypeDef", self.owner[0]) if kind == "T" else ("MethodDef", self.owner[1])
            name = self.a.gparams.get(key, {}).get(n)
            if name:
                return name
        return f"{'!' if kind == 'T' else '!!'}{n}"

    def type(self):
        e = self.u8()
        if e in PRIM:
            return PRIM[e]
        if e in (0x11, 0x12):
            return self.tok().split("`")[0]
        if e == 0x0F:
            return self.type() + "*"
        if e == 0x10:
            return "ref " + self.type()
        if e == 0x1D:
            return self.type() + "[]"
        if e == 0x13:
            return self.gname("T", self.cu())
        if e == 0x1E:
            return self.gname("M", self.cu())
        if e == 0x15:
            self.u8()
            base = self.tok().split("`")[0]
            args = [self.type() for _ in range(self.cu())]
            if base == "Nullable" and len(args) == 1:
                return args[0] + "?"
            return f"{base}<{', '.join(args)}>"
        if e == 0x14:
            t = self.type()
            rank = self.cu()
            for _ in range(self.cu()):
                self.cu()
            for _ in range(self.cu()):
                self.cu()
            return t + "[" + "," * (rank - 1) + "]"
        if e in (0x1F, 0x20):
            self.tok()
            return self.type()
        if e == 0x45:
            return self.type()
        if e == 0x1B:
            return "delegate*"
        return f"?0x{e:02x}"

    def method(self):
        cc = self.u8()
        if cc & 0x10:
            self.cu()
        n = self.cu()
        ret = self.type()
        return ret, [self.type() for _ in range(n)]

    def field(self):
        self.u8()
        return self.type()

    def prop(self):
        self.u8()
        n = self.cu()
        t = self.type()
        return t, [self.type() for _ in range(n)]


def range_rows(table_list, start_attr_rows, idx, total):
    pass


def member_rows(asm, tidx, kind):
    t = asm.t
    rows = getattr(t.TypeDef[tidx - 1], kind)
    return [i.row_index for i in rows]


VIS_M = [("mdPublic", "public"), ("mdFamORAssem", "protected internal"), ("mdFamily", "protected"),
         ("mdAssem", "internal"), ("mdFamANDAssem", "private protected"), ("mdPrivate", "private")]
VIS_F = [("fdPublic", "public"), ("fdFamORAssem", "protected internal"), ("fdFamily", "protected"),
         ("fdAssembly", "internal"), ("fdFamANDAssem", "private protected"), ("fdPrivate", "private")]


def vis(flags, table):
    for attr, name in table:
        if getattr(flags, attr, False):
            return name
    return "private"


def visible(v, show_all):
    return show_all or v in ("public", "protected", "protected internal")


def type_kind(asm, r):
    f = r.Flags
    if f.tdInterface:
        return "interface"
    base = asm.tdor(r.Extends) if r.Extends and r.Extends.table else ""
    if base == "Enum":
        return "enum"
    if base == "ValueType":
        return "struct"
    if base == "MulticastDelegate":
        return "delegate"
    mods = ("static " if f.tdAbstract and f.tdSealed else "abstract " if f.tdAbstract else "sealed " if f.tdSealed else "")
    return mods + "class"


def type_vis(r):
    f = r.Flags
    if f.tdPublic or f.tdNestedPublic:
        return "public"
    if f.tdNestedFamily:
        return "protected"
    return "internal/private"


def dump_type(asm, tidx, show_all):
    r = asm.t.TypeDef[tidx - 1]
    full = asm.typedef_name(tidx)
    kind = type_kind(asm, r)
    gp = asm.gparams.get(("TypeDef", tidx))
    gen = f"<{', '.join(gp[k] for k in sorted(gp))}>" if gp else ""
    bases = []
    if r.Extends and r.Extends.table:
        b = asm.tdor(r.Extends)
        if b not in ("Object", "ValueType", "Enum"):
            bases.append(b)
    bases += [asm.tdor(i) for i in asm.ifaces.get(tidx, [])]
    out = [f"// {asm.name}", f"{type_vis(r)} {kind} {full.split('`')[0]}{gen}" + (f" : {', '.join(bases)}" if bases else "")]
    owner_t = tidx
    is_enum = kind == "enum"
    for fi in member_rows(asm, tidx, "FieldList"):
        f = asm.t.Field[fi - 1]
        v = vis(f.Flags, VIS_F)
        name = str(f.Name)
        if is_enum:
            if name == "value__":
                continue
            ctype, cval = asm.consts.get(fi, (None, b""))
            val = int.from_bytes(cval, "little", signed=True) if cval else "?"
            out.append(f"    {name} = {val}")
            continue
        if not visible(v, show_all) or name.startswith("<"):
            continue
        ft = Sig(asm, f.Signature.value, (owner_t, 0)).field()
        mods = ("const " if f.Flags.fdLiteral else "") + ("static " if f.Flags.fdStatic and not f.Flags.fdLiteral else "") + ("readonly " if f.Flags.fdInitOnly else "")
        out.append(f"    {v} {mods}{ft} {name};")
    methods = member_rows(asm, tidx, "MethodList")
    mnames = {}
    for mi in methods:
        m = asm.t.MethodDef[mi - 1]
        mnames[str(m.Name)] = vis(m.Flags, VIS_M)
    for pi in asm.props.get(tidx, []):
        p = asm.t.Property[pi - 1]
        name = str(p.Name)
        gv, sv = mnames.get("get_" + name), mnames.get("set_" + name)
        if not (gv and visible(gv, show_all)) and not (sv and visible(sv, show_all)):
            continue
        pt, _ = Sig(asm, p.Type.value, (owner_t, 0)).prop()
        acc = []
        if gv:
            acc.append(("" if gv == "public" else gv + " ") + "get;")
        if sv:
            acc.append(("" if sv == "public" else sv + " ") + "set;")
        out.append(f"    {pt} {name} {{ {' '.join(acc)} }}")
    for ei in asm.events.get(tidx, []):
        e = asm.t.Event[ei - 1]
        name = str(e.Name)
        av = mnames.get("add_" + name)
        if av and visible(av, show_all):
            out.append(f"    {av} event {asm.tdor(e.EventType)} {name};")
    for mi in methods:
        m = asm.t.MethodDef[mi - 1]
        name = str(m.Name)
        v = vis(m.Flags, VIS_M)
        if not visible(v, show_all):
            continue
        if m.Flags.mdSpecialName and re.match(r"(get_|set_|add_|remove_)", name):
            continue
        try:
            ret, params = Sig(asm, m.Signature.value, (owner_t, mi)).method()
        except Exception as ex:  # noqa: BLE001
            ret, params = f"<sig error {ex}>", []
        pnames = {}
        for pr in getattr(m, "ParamList", []) or []:
            prow = asm.t.Param[pr.row_index - 1]
            pnames[prow.Sequence] = str(prow.Name)
        args = ", ".join(f"{t} {pnames.get(i + 1, 'p' + str(i))}" for i, t in enumerate(params))
        mg = asm.gparams.get(("MethodDef", mi))
        mgen = f"<{', '.join(mg[k] for k in sorted(mg))}>" if mg else ""
        mods = ("static " if m.Flags.mdStatic else "") + ("abstract " if m.Flags.mdAbstract else "virtual " if m.Flags.mdVirtual and not m.Flags.mdFinal else "")
        if name == ".ctor":
            out.append(f"    {v} {full.split('.')[-1].split('`')[0]}({args});")
        elif name == ".cctor":
            continue
        else:
            out.append(f"    {v} {mods}{ret} {name}{mgen}({args});")
    return "\n".join(out)


def load(target):
    paths = [os.path.join(target, f) for f in sorted(os.listdir(target)) if f.endswith(".dll")] if os.path.isdir(target) else [target]
    asms = []
    for p in paths:
        try:
            a = Asm(p)
            if a.pe.net and a.t.TypeDef:
                asms.append(a)
        except Exception:  # noqa: BLE001  non-.NET or unreadable
            pass
    return asms


def main(argv):
    show_all = "--all" in argv
    argv = [a for a in argv if a != "--all"]
    if len(argv) < 3:
        print(__doc__)
        return 2
    target, cmd, q = argv[0], argv[1], argv[2]
    asms = load(target)
    found = 0
    if cmd == "type":
        for a in asms:
            for i in range(1, len(a.t.TypeDef) + 1):
                full = a.typedef_name(i).split("`")[0]
                if full == q or full.split(".")[-1].split("+")[-1] == q or full.endswith("." + q):
                    print(dump_type(a, i, show_all))
                    print()
                    found += 1
    elif cmd == "find":
        rx = re.compile(q)
        for a in asms:
            for i in range(1, len(a.t.TypeDef) + 1):
                full = a.typedef_name(i).split("`")[0]
                if rx.search(full):
                    print(f"[type] {full}  ({a.name})")
                    found += 1
                for mi in member_rows(a, i, "MethodList"):
                    m = a.t.MethodDef[mi - 1]
                    if rx.search(str(m.Name)) and (show_all or visible(vis(m.Flags, VIS_M), False)):
                        print(f"[method] {full}::{m.Name}  ({vis(m.Flags, VIS_M)}{' static' if m.Flags.mdStatic else ''})")
                        found += 1
                for fi in member_rows(a, i, "FieldList"):
                    f = a.t.Field[fi - 1]
                    if rx.search(str(f.Name)) and (show_all or visible(vis(f.Flags, VIS_F), False)):
                        print(f"[field] {full}::{f.Name}  ({vis(f.Flags, VIS_F)})")
                        found += 1
                for pi in a.props.get(i, []):
                    p = a.t.Property[pi - 1]
                    if rx.search(str(p.Name)):
                        print(f"[property] {full}::{p.Name}")
                        found += 1
    else:
        print(__doc__)
        return 2
    if not found:
        print("no matches")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
