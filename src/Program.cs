// Decompiled with JetBrains decompiler
// Type: sh4_asm.Program
// Assembly: sh4_asm, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null
// MVID: 52066F41-EA72-4C12-B6F3-5FED11FB8217
// Assembly location: G:\Files\Programs\DC_Tools\binhack32\sh4_asm.exe

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace sh4_asm
{
    public class Program
    {
        private static List<Program.Expression> expression_table;
        private static Dictionary<string, Program.Symbol> symbol_table;
        private static uint starting_offset;
        private static Dictionary<string, Program.Module> modules_loaded;
        private static string working_directory;
        private static Program.Endian endian;
        private static Dictionary<string, long> setting_dict = new Dictionary<string, long>();
        private static Program.IfStatus if_status;

        private static void Main(string[] args)
        {
            Program.endian = Program.Endian.Little;
            Program.if_status = Program.IfStatus.not_in_if_block;
            Program.starting_offset = 216203264U;

            if (args.Length < 2)
            {
                Console.WriteLine("need input filename, output filename.\nthird option is offset, is optional");
            }
            else
            {
                if (args.Length >= 3)
                {
                    args[2] = args[2].ToUpperInvariant();
                    Program.starting_offset = !args[2].StartsWith("0X") ? uint.Parse(args[2], (IFormatProvider)CultureInfo.InvariantCulture) : Convert.ToUInt32(args[2], 16);
                }
                Program.working_directory = Path.GetDirectoryName(Path.GetFullPath(args[0]));
                Console.WriteLine("asm using offset " + Program.starting_offset.ToString("X2"));
                Program.init_symbols();
                Program.modules_loaded = new Dictionary<string, Program.Module>();
                Program.Module module = new Program.Module();
                module.name = Path.GetFileNameWithoutExtension(args[0]).ToUpperInvariant();
                module.statement_number_offset = 0;
                Program.modules_loaded.Add(module.name, module);
                List<Program.Statement> input;
                using (StreamReader reader = File.OpenText(args[0]))
                    input = Program.tokenize_and_parse(reader, module.name, 0);
                List<Program.Statement> output = new List<Program.Statement>(input.Count * 2);
                Program.handle_module_loading(input, output, 0);
                List<Program.Statement> statements = output;
                Program.fix_associated_labels(statements);
                Program.intermediate_step(statements);
                string tempFileName1 = Path.GetTempFileName();
                using (BinaryWriter writer = new BinaryWriter((Stream)new MemoryStream()))
                {
                    if (args.Length >= 4)
                    {
                        Console.WriteLine("writing asm output to " + args[3]);
                        string tempFileName2 = Path.GetTempFileName();
                        using (StreamWriter log_writer = new StreamWriter((Stream)File.Open(tempFileName2, FileMode.Create)))
                            Program.code_generation(statements, writer, (TextWriter)log_writer);
                        File.Delete(args[3] + ".bak");
                        if (File.Exists(args[3]))
                            File.Copy(args[3], args[3] + ".bak");
                        File.Delete(args[3]);
                        File.Copy(tempFileName2, args[3]);
                    }
                    else
                        Program.code_generation(statements, writer);
                    using (FileStream fileStream = File.Open(tempFileName1, FileMode.Create))
                    {
                        writer.Flush();
                        ((MemoryStream)writer.BaseStream).WriteTo((Stream)fileStream);
                        fileStream.Flush();
                    }
                }
                File.Delete(args[1] + ".bak");
                if (File.Exists(args[1]))
                    File.Copy(args[1], args[1] + ".bak");
                File.Delete(args[1]);
                File.Copy(tempFileName1, args[1]);
                Program.save_symbol_table(statements, Path.Combine(Program.working_directory, Path.GetFileNameWithoutExtension(args[0]) + ".symbol_table"));
            }
        }

        private static void save_symbol_table(List<Program.Statement> statements, string filename)
        {
            string tempFileName = Path.GetTempFileName();
            using (BinaryWriter writer = new BinaryWriter((Stream)File.Open(tempFileName, FileMode.Create)))
                Program.generate_symbol_table_to_binary_stream(statements, writer);
            File.Delete(filename + ".bak");
            if (File.Exists(filename))
                File.Copy(filename, filename + ".bak");
            File.Delete(filename);
            File.Copy(tempFileName, filename);
        }

        private static void generate_symbol_table_to_text_stream(
          List<Program.Statement> statements,
          StreamWriter writer)
        {
            foreach (Program.Symbol symbol in Program.symbol_table.Values)
            {
                if (symbol.symbol_type == Program.SymbolType.label)
                {
                    writer.Write("#symbol ");
                    writer.Write(symbol.name.ToLowerInvariant());
                    writer.Write(" 0x");
                    writer.Write(symbol.value.ToString("X2"));
                    writer.WriteLine();
                }
            }
        }

        private static void generate_symbol_table_to_binary_stream(
          List<Program.Statement> statements,
          BinaryWriter writer)
        {
            Dictionary<string, int> dictionary = new Dictionary<string, int>(Program.modules_loaded.Count);
            writer.Write("TABL".ToCharArray());
            writer.Write(1L);
            int count = Program.modules_loaded.Count;
            writer.Write(Program.modules_loaded.Count);
            int num1 = 0;
            foreach (Program.Module module in Program.modules_loaded.Values)
            {
                writer.Write(module.name);
                dictionary.Add(module.name, num1);
                ++num1;
            }
            int num2 = 0;
            foreach (Program.Symbol symbol in Program.symbol_table.Values)
            {
                if (symbol.symbol_type == Program.SymbolType.label)
                    ++num2;
            }
            writer.Write(num2);
            foreach (Program.Symbol symbol in Program.symbol_table.Values)
            {
                if (symbol.symbol_type == Program.SymbolType.label)
                {
                    if (count <= (int)byte.MaxValue)
                        writer.Write((byte)dictionary[symbol.module]);
                    else if (count <= (int)ushort.MaxValue)
                        writer.Write((ushort)dictionary[symbol.module]);
                    else
                        writer.Write(dictionary[symbol.module]);
                    writer.Write(symbol.short_name);
                    writer.Write((uint)symbol.value);
                    //if ( ((uint)symbol.value) > 0x8C1C9DAE) { 
                    //    Console.WriteLine(symbol.short_name + " @ " + String.Format("0x{0:X}", (uint)symbol.value));
                    //    Console.WriteLine();
                    //}
                }
            }
        }

        private static void output_ranges_that_might_be_code(List<Program.Statement> statements)
        {
            HashSet<uint> uintSet = new HashSet<uint>();
            foreach (Program.Statement statement in statements)
            {
                long addressIfJump = Program.get_address_if_jump(statement);
                if (addressIfJump >= 0L)
                    // Console.WriteLine((uint)addressIfJump);
                    uintSet.Add((uint)addressIfJump);
            }
            bool flag = true;
            uint num = 0;
            foreach (Program.Statement statement in statements)
            {
                if (flag)
                {
                    if (uintSet.Contains(statement.address) && statement.instruction == "#DATA")
                    {
                        num = statement.address;
                        flag = false;
                    }
                }
                else if (Program.symbol_table[statement.instruction].symbol_type == Program.SymbolType.instruction)
                {
                    flag = true;
                    Console.WriteLine("code?? " + num.ToString("X2") + " to " + statement.address.ToString("X2"));
                }
            }
        }

        private static long get_address_if_jump(Program.Statement statement)
        {
            switch (statement.instruction)
            {
                case "BF":
                case "BF.S":
                case "BF/S":
                case "BRA":
                case "BSR":
                case "BT":
                case "BT.S":
                case "BT/S":
                    return statement.tokens[0].value;
                default:
                    return -1;
            }
        }

        private static void output_labels_for_unlabeled(List<Program.Statement> statements)
        {
            foreach (Program.Statement statement in statements)
            {
                long addressIfRaw = Program.get_address_if_raw(statement);
                if (addressIfRaw > 0L)
                {
                    Console.WriteLine("note, for line " + (object)statement.line_number + " (" + statement.module + "):");
                    Console.WriteLine(statement.raw_line);
                    for (int index = 0; index < statements.Count; ++index)
                    {
                        if ((long)statements[index].address == addressIfRaw && statements[index].instruction != "#ALIGN4" && statements[index].instruction != "#ALIGN" && statements[index].instruction != "#ALIGN4_NOP" && statements[index].instruction != "#ALIGN16" && statements[index].instruction != "#ALIGN16_NOP")
                        {
                            Console.WriteLine(" can add loc_" + addressIfRaw.ToString("X2").ToLowerInvariant() + ": before line " + (object)statements[index].line_number + " of " + statements[index].module);
                            Console.WriteLine(statements[index].raw_line);
                            break;
                        }
                        if ((long)statements[index].address > addressIfRaw)
                        {
                            Console.WriteLine(statements[index].address.ToString("X2").ToLowerInvariant() + " is greater than expected addr of " + addressIfRaw.ToString("X2").ToLowerInvariant());
                            Console.WriteLine(" can add loc_" + addressIfRaw.ToString("X2").ToLowerInvariant() + ": near??/before??? line " + (object)statements[index].line_number + " of " + statements[index].module);
                            Console.WriteLine(statements[index].raw_line);
                            Console.WriteLine("(note: this may be because the previous line is larger than 2 bytes or something else?)");
                            Console.WriteLine("previous line:");
                            Console.WriteLine(statements[index - 1].raw_line);
                            break;
                        }
                    }
                    Console.WriteLine();
                }
            }
        }

        private static long get_address_if_raw(Program.Statement statement)
        {
            switch (statement.instruction)
            {
                case "BF":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -128, 127) * 2) + (long)statement.address + 4L;
                    break;
                case "BF.S":
                case "BF/S":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -128, 127) * 2) + (long)statement.address + 4L;
                    break;
                case "BRA":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -2048, 2047) * 2) + (long)statement.address + 4L;
                    break;
                case "BSR":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -2048, 2047) * 2) + (long)statement.address + 4L;
                    break;
                case "BT":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -128, 127) * 2) + (long)statement.address + 4L;
                    break;
                case "BT.S":
                case "BT/S":
                    if (Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                        return (long)(Program.calculate_pc_displacement(statement, 2, -128, 127) * 2) + (long)statement.address + 4L;
                    break;
                case "MOV.L":
                    if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct) && statement.tokens[0].inner_token.parse_type != Program.ParseType.name)
                        return (long)(Program.calculate_pc_displacement(statement, 4, 0, 255) * 4) + (long)statement.address + 4L & 0xFFFFFFFC;
                        
                    break;
                case "MOV.W":
                    if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct) && statement.tokens[0].inner_token.parse_type != Program.ParseType.name)
                        return (long)(Program.calculate_pc_displacement(statement, 2, 0, 255) * 2) + (long)statement.address + 4L;
                    break;
                case "MOVA":
                    if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct) && statement.tokens[0].inner_token.parse_type != Program.ParseType.name)
                        return (long)(Program.calculate_pc_displacement(statement, 4, 0, 255) * 4) + (long)statement.address + 4L & 0xFFFFFFFC;
                    break;
                default:
                    return -1;
            }
            return -1;
        }

        private static void code_generation(
          List<Program.Statement> statements,
          BinaryWriter writer,
          TextWriter log_writer = null)
        {
            long offset = 0;
            Dictionary<uint, List<Program.Symbol>> dictionary = new Dictionary<uint, List<Program.Symbol>>();
            if (log_writer != null)
            {
                foreach (KeyValuePair<string, Program.Symbol> keyValuePair in Program.symbol_table)
                {
                    if (keyValuePair.Value.symbol_type == Program.SymbolType.label)
                    {
                        if (!dictionary.ContainsKey(keyValuePair.Value.address))
                            dictionary.Add(keyValuePair.Value.address, new List<Program.Symbol>());
                        dictionary[keyValuePair.Value.address].Add(keyValuePair.Value);
                    }
                }
            }
            foreach (Program.Statement statement in statements)
            {
                if (log_writer != null)
                {
                    log_writer.WriteLine(";;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;");
                    log_writer.Write("; line number: ");
                    log_writer.Write(statement.line_number);
                    log_writer.Write("\n; at 'memory' address ");
                    log_writer.Write(statement.address.ToString("X4"));
                    log_writer.Write(", file output address ");
                    log_writer.Write(writer.BaseStream.Position.ToString("X4"));
                    log_writer.Write(", module: ");
                    log_writer.Write(statement.module);
                    log_writer.Write("\n");
                    if (dictionary.ContainsKey(statement.address))
                    {
                        log_writer.Write(";labels:\n");
                        foreach (Program.Symbol symbol in dictionary[statement.address])
                        {
                            log_writer.Write("\t");
                            log_writer.Write(symbol.name);
                            log_writer.Write(":\n");
                        }
                    }
                    log_writer.Write(";input line:\n;  ");
                    log_writer.Write(statement.raw_line);
                    log_writer.Write("\n; tokenized:\n");
                    log_writer.Write("\t");
                    log_writer.Write(statement.instruction);
                    log_writer.Write(" ");
                    foreach (Program.Token token in statement.tokens)
                    {
                        log_writer.Write(token.raw_string);
                        log_writer.Write(" ");
                    }
                    log_writer.WriteLine();
                    log_writer.Write("; argument values: ");
                    foreach (Program.Token token in statement.tokens)
                    {
                        log_writer.Write(token.value);
                        if (token.inner_token != null)
                        {
                            log_writer.Write("(");
                            log_writer.Write(token.inner_token.value.ToString("X2"));
                            if (token.inner_token2 != null)
                            {
                                log_writer.Write(",");
                                log_writer.Write(token.inner_token2.value.ToString("X2"));
                            }
                            log_writer.Write(")");
                        }
                        log_writer.Write(" ");
                    }
                    log_writer.WriteLine();
                    offset = writer.BaseStream.Position;
                }
                for (int index = 0; index < statement.repeat_count; ++index)
                    Program.generate_statement(statement, writer);
                if (log_writer != null)
                {
                    log_writer.Write("; actual output: ");
                    using (MemoryStream input = new MemoryStream())
                    {
                        writer.BaseStream.Flush();
                        ((MemoryStream)writer.BaseStream).WriteTo((Stream)input);
                        input.Flush();
                        using (BinaryReader binaryReader = new BinaryReader((Stream)input))
                        {
                            binaryReader.BaseStream.Seek(offset, SeekOrigin.Begin);
                            foreach (byte readByte in binaryReader.ReadBytes((int)(writer.BaseStream.Position - offset)))
                            {
                                log_writer.Write(readByte.ToString("X2"));
                                log_writer.Write(" ");
                            }
                            log_writer.WriteLine();
                            log_writer.WriteLine(";;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;;");
                            log_writer.WriteLine();
                            log_writer.WriteLine();
                        }
                    }
                }
            }
        }

        private static void generate_statement(Program.Statement statement, BinaryWriter writer)
        {
            if (statement.instruction == "#DATA" || statement.instruction == "#DATA8" || statement.instruction == "#DATA16")
                Program.generate_data(statement, writer);
            // Pads to a certain value //
            else if (statement.instruction == "#PAD_TO")
            {
                uint target = (uint)statement.tokens[0].value;
                uint current = statement.address;
                uint pad_value = 0;
                if (current > target)
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, statement.instruction + " target position is before current position.");

                // Optional 3rd parameter for which value is written //
                if (statement.tokens.Count > 1)
                {
                    pad_value = (uint)statement.tokens[1].value;
                }

                for (; current < target; current++)
                {
                    writer.Write((byte)pad_value);
                }
            }
            // Pads to a certain value with NOPs //
            else if (statement.instruction == "#NOP_TO")
            {
                uint target = (uint)statement.tokens[0].value;
                uint current = statement.address;

                uint mod4 = current % 4;
                if (mod4 % 2U == 1U)
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, statement.instruction + " must be 2-aligned already.");

                if (current > target)
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, statement.instruction + " target position is before current position.");

                for (; current < target; current += 2U)
                {
                    if (Program.endian == Program.Endian.Little)
                    {
                        writer.Write((byte)9);
                        writer.Write((byte)0);
                    }
                    else
                    {
                        writer.Write((byte)0);
                        writer.Write((byte)9);
                    }
                }
            }
            else if (statement.instruction == "#ALIGN4" || statement.instruction == "#ALIGN16")
            {
                uint num1 = 4;
                if (statement.instruction == "#ALIGN16")
                    num1 = 16U;
                uint num2 = statement.address % num1;
                uint num3 = 0;
                for (; num2 > 0U; num2 = (statement.address + num3) % num1)
                {
                    writer.Write((byte)0);
                    ++num3;
                }
            }
            else if (statement.instruction == "#ALIGN4_NOP" || statement.instruction == "#ALIGN16_NOP")
            {
                uint num4 = 4;
                if (statement.instruction == "#ALIGN16_NOP")
                    num4 = 16U;
                uint num5 = statement.address % num4;
                if (num5 % 2U == 1U)
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, statement.instruction + " must be 2-aligned already.");
                uint num6 = 0;
                for (; num5 > 0U; num5 = (statement.address + num6) % num4)
                {
                    if (Program.endian == Program.Endian.Little)
                    {
                        writer.Write((byte)9);
                        writer.Write((byte)0);
                    }
                    else
                    {
                        writer.Write((byte)0);
                        writer.Write((byte)9);
                    }
                    num6 += 2U;
                }
            }
            else if (statement.instruction == "#ALIGN")
            {
                uint num7 = statement.address % 2U;
                uint num8 = 0;
                for (; num7 > 0U; num7 = (statement.address + num8) % 2U)
                {
                    writer.Write((byte)0);
                    ++num8;
                }
            }
            else if (statement.instruction == "#IMPORT_RAW_DATA")
                Program.generate_import_raw_data(statement, writer);
            else if (statement.instruction == "#BIG_ENDIAN")
                Program.endian = Program.Endian.Big;
            else if (statement.instruction == "#LITTLE_ENDIAN")
                Program.endian = Program.Endian.Little;
            else if (statement.instruction.StartsWith("#"))
            {
                if (Program.symbol_table.ContainsKey(statement.instruction) && Program.symbol_table[statement.instruction].symbol_type == Program.SymbolType.builtin)
                    return;
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "unknown directive " + statement.instruction);
            }
            else
            {
                ushort num = Program.generate_instruction(statement);
                if (Program.endian == Program.Endian.Big)
                    num = (ushort)((int)num >> 8 & (int)ushort.MaxValue | (int)num << 8);
                writer.Write(num);
            }
        }

        private static void generate_import_raw_data(Program.Statement statement, BinaryWriter writer)
        {
            if (statement.tokens != null && statement.tokens.Count == 1)
            {
                Program.Token token = statement.tokens[0];
                if (token.parse_type == Program.ParseType.string_data)
                {
                    string str = Path.Combine(Program.working_directory, token.raw_string);
                    if (!File.Exists(str))
                        return;
                    long length = new FileInfo(str).Length;
                    if (length != token.value)
                    {
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, str + " changed size during compilation?");
                    }
                    else
                    {
                        using (BinaryReader binaryReader = new BinaryReader((Stream)File.Open(str, FileMode.Open)))
                        {
                            for (int index = 0; index < statement.repeat_count; ++index)
                            {
                                writer.Write(binaryReader.ReadBytes((int)length));
                                binaryReader.BaseStream.Seek(0L, SeekOrigin.Begin);
                            }
                        }
                    }
                }
                else
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, token.raw_string + " is a symbol that exists, but not of the right type to use for #data");
            }
            else
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "wrong number of inputs for " + statement.instruction + " directive, need a single filename");
        }

        private static void generate_data(Program.Statement statement, BinaryWriter writer)
        {
            uint address = statement.address;
            foreach (Program.Token token in statement.tokens)
            {
                if (token.parse_type == Program.ParseType.string_data)
                {
                    writer.Write(Encoding.ASCII.GetBytes(token.raw_string));
                    address += token.size;
                }
                else
                {
                    if (Program.endian == Program.Endian.Little)
                    {
                        switch (token.size)
                        {
                            case 1:
                                writer.Write((byte)token.value);
                                break;
                            case 2:
                                writer.Write((short)token.value);
                                break;
                            case 4:
                                writer.Write((int)token.value);
                                break;
                            case 8:
                                writer.Write(token.value);
                                break;
                            default:
                                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "Data chunk sizes other than 1, 2, 4, or 8 bytes not currently supported. (\"" + token.raw_string + "\" is " + (object)token.size + " bytes)");
                                break;
                        }
                    }
                    else
                    {
                        switch (token.size)
                        {
                            case 1:
                                writer.Write((byte)token.value);
                                break;
                            case 2:
                                ushort num1 = (ushort)token.value;
                                ushort num2 = (ushort)(((int)num1 & (int)byte.MaxValue) << 8 | ((int)num1 & 65280) >> 8);
                                writer.Write(num2);
                                break;
                            case 4:
                                uint num3 = (uint)token.value;
                                uint num4 = (uint)(((int)num3 & (int)byte.MaxValue) << 24 | ((int)num3 & 65280) << 8) | (num3 & 16711680U) >> 8 | (num3 & 4278190080U) >> 24;
                                writer.Write(num4);
                                break;
                            case 8:
                                ulong num5 = (ulong)token.value;
                                ulong num6 = (ulong)(((long)num5 & (long)byte.MaxValue) << 56 | ((long)num5 & 65280L) << 40 | ((long)num5 & 16711680L) << 24 | ((long)num5 & 4278190080L) << 8) | (num5 & 1095216660480UL) >> 8 | (num5 & 280375465082880UL) >> 24 | (num5 & 71776119061217280UL) >> 40 | (num5 & 18374686479671623680UL) >> 56;
                                writer.Write(num6);
                                break;
                            default:
                                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "Data chunk sizes other than 1, 2, 4, or 8 bytes not currently supported. (\"" + token.raw_string + "\" is " + (object)token.size + " bytes)");
                                break;
                        }
                    }
                    if (address % token.size != 0U)
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "Data chunk size is " + (object)token.size + ", but address (0x" + statement.address.ToString("X4") + ") is not aligned to that size. off by " + (object)(address % token.size) + " (\"" + token.raw_string + "\" is " + (object)token.size + " bytes).\ntry adding some NOPs or 1byte data to pad?");
                    address += token.size;
                }
            }
        }

        private static ushort generate_instruction(Program.Statement statement)
        {
            List<Program.Token> tokens = statement.tokens;
            switch (statement.instruction)
            {
                case "ADD":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                    {
                        return Program.generate_register_register_swapped((ushort)12300, statement);
                    }

                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        //Console.WriteLine(statement.raw_line);
                        //Console.WriteLine("Detecting immediate.");
                        return Program.generate_immediate_register((ushort)7, statement);
                    }
                    break;
                case "ADDC":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12302, statement);
                    break;
                case "ADDV":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12303, statement);
                    break;
                case "AND":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8201, statement);
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)201, statement);
                    }
                    break;
                case "AND.B":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.gbr_indirect_indexed))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)205, statement);
                    }
                    break;
                case "BF":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement8((ushort)139, statement);
                case "BF.S":
                case "BF/S":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement8((ushort)143, statement);
                case "BRA":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement12((ushort)10, statement);
                case "BRAF":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)35, statement, 0);
                    break;
                case "BSR":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement12((ushort)11, statement);
                case "BSRF":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)3, statement, 0);
                    break;
                case "BT":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement8((ushort)137, statement);
                case "BT.S":
                case "BT/S":
                    if (!Program.check_arguments(statement, Program.ParseType.integer_number))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.absolute_displacement_address))
                            break;
                    }
                    return Program.generate_pc_displacement8((ushort)141, statement);
                case "CLRMAC":
                    if (Program.check_arguments(statement))
                        return 40;
                    break;
                case "CLRS":
                    if (Program.check_arguments(statement))
                        return 72;
                    break;
                case "CLRT":
                    if (Program.check_arguments(statement))
                        return 8;
                    break;
                case "CMP/EQ":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12288, statement);
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)136, statement);
                    }
                    break;
                case "CMP/GE":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12291, statement);
                    break;
                case "CMP/GT":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12295, statement);
                    break;
                case "CMP/HI":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12294, statement);
                    break;
                case "CMP/HS":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12290, statement);
                    break;
                case "CMP/PL":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16405, statement, 0);
                    break;
                case "CMP/PZ":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16401, statement, 0);
                    break;
                case "CMP/STR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8204, statement);
                    break;
                case "DIV0S":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8199, statement);
                    break;
                case "DIV0U":
                    if (Program.check_arguments(statement))
                        return 25;
                    break;
                case "DIV1":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12292, statement);
                    break;
                case "DMULS.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12301, statement);
                    break;
                case "DMULU.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12293, statement);
                    break;
                case "DT":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16400, statement, 0);
                    break;
                case "EXTS.B":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24590, statement);
                    break;
                case "EXTS.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24591, statement);
                    break;
                case "EXTU.B":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24588, statement);
                    break;
                case "EXTU.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24589, statement);
                    break;
                case "FABS":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register((ushort)61533, statement, 0);
                case "FADD":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61440, statement);
                case "FCMP/EQ":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61444, statement);
                case "FCMP/GT":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61445, statement);
                case "FCNVDS":
                    if (Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61629, statement, 0);
                    break;
                case "FCNVSD":
                    if (Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.dr_register_direct) && tokens[0].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61613, statement, 1);
                    break;
                case "FDIV":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61443, statement);
                case "FIPR":
                    if (Program.check_arguments(statement, Program.ParseType.fv_register_direct, Program.ParseType.fv_register_direct))
                        return Program.generate_fv_register_register((ushort)61677, statement);
                    break;
                case "FLDI0":
                    if (Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                        return Program.generate_register((ushort)61581, statement, 0);
                    break;
                case "FLDI1":
                    if (Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                        return Program.generate_register((ushort)61597, statement, 0);
                    break;
                case "FLDS":
                    if (Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61469, statement, 0);
                    break;
                case "FLOAT":
                    if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.dr_register_direct))
                            break;
                    }
                    if (tokens[0].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61485, statement, 1);
                    break;
                case "FMAC":
                    if (Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 0, "FR0");
                        return Program.generate_register_register_swapped((ushort)61454, statement, 1);
                    }
                    break;
                case "FMOV":
                case "FMOV.S":
                    if (Program.check_arguments(statement, Program.ParseType.xd_register_direct, Program.ParseType.register_indirect))
                        return Program.generate_register_register_swapped((ushort)61466, statement);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect, Program.ParseType.xd_register_direct))
                        return Program.generate_register_register_swapped((ushort)61704, statement);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.xd_register_direct))
                        return Program.generate_register_register_swapped((ushort)61705, statement);
                    if (Program.check_arguments(statement, Program.ParseType.xd_register_direct, Program.ParseType.register_indirect_pre_decrement))
                        return Program.generate_register_register_swapped((ushort)61467, statement);
                    if (Program.check_arguments(statement, Program.ParseType.register_indexed_indirect, Program.ParseType.xd_register_direct))
                        return Program.generate_register_register_swapped((ushort)61702, statement);
                    if (Program.check_arguments(statement, Program.ParseType.xd_register_direct, Program.ParseType.register_indexed_indirect))
                        return Program.generate_register_register_swapped((ushort)61463, statement);
                    if (Program.check_arguments(statement, Program.ParseType.xd_register_direct, Program.ParseType.xd_register_direct))
                        return Program.generate_register_register_swapped((ushort)61724, statement);
                    if (Program.check_arguments(statement, Program.ParseType.xd_register_direct, Program.ParseType.dr_register_direct))
                        return Program.generate_register_register_swapped((ushort)61468, statement);
                    if (Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.xd_register_direct))
                        return Program.generate_register_register_swapped((ushort)61708, statement);
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                        {
                            if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.register_indirect))
                            {
                                if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.register_indirect))
                                {
                                    if (!Program.check_arguments(statement, Program.ParseType.register_indirect, Program.ParseType.fr_register_direct))
                                    {
                                        if (!Program.check_arguments(statement, Program.ParseType.register_indirect, Program.ParseType.dr_register_direct))
                                        {
                                            if (!Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.fr_register_direct))
                                            {
                                                if (!Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.dr_register_direct))
                                                {
                                                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.register_indirect_pre_decrement))
                                                    {
                                                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.register_indirect_pre_decrement))
                                                        {
                                                            if (!Program.check_arguments(statement, Program.ParseType.register_indexed_indirect, Program.ParseType.fr_register_direct))
                                                            {
                                                                if (!Program.check_arguments(statement, Program.ParseType.register_indexed_indirect, Program.ParseType.dr_register_direct))
                                                                {
                                                                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.register_indexed_indirect))
                                                                    {
                                                                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.register_indexed_indirect))
                                                                            break;
                                                                    }
                                                                    return Program.generate_register_register_swapped((ushort)61447, statement);
                                                                }
                                                            }
                                                            return Program.generate_register_register_swapped((ushort)61446, statement);
                                                        }
                                                    }
                                                    return Program.generate_register_register_swapped((ushort)61451, statement);
                                                }
                                            }
                                            return Program.generate_register_register_swapped((ushort)61449, statement);
                                        }
                                    }
                                    return Program.generate_register_register_swapped((ushort)61448, statement);
                                }
                            }
                            return Program.generate_register_register_swapped((ushort)61450, statement);
                        }
                    }
                    return Program.generate_register_register_swapped((ushort)61452, statement);
                case "FMUL":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61442, statement);
                case "FNEG":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register((ushort)61517, statement, 0);
                case "FRCHG":
                    if (Program.check_arguments(statement))
                        return 64509;
                    break;
                case "FSCA":
                    if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.dr_register_direct))
                            break;
                    }
                    if (tokens[0].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61693, statement, 1);
                    break;
                case "FSCHG":
                    if (Program.check_arguments(statement))
                        return 62461;
                    break;
                case "FSQRT":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register((ushort)61549, statement, 0);
                case "FSRRA":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register((ushort)61565, statement, 0);
                case "FSTS":
                    if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.dr_register_direct))
                            break;
                    }
                    if (tokens[0].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61453, statement, 1);
                    break;
                case "FSUB":
                    if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.fr_register_direct))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.dr_register_direct))
                            break;
                    }
                    return Program.generate_register_register_swapped((ushort)61441, statement);
                case "FTRC":
                    if (!Program.check_arguments(statement, Program.ParseType.dr_register_direct, Program.ParseType.other_register))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.fr_register_direct, Program.ParseType.other_register))
                            break;
                    }
                    if (tokens[1].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)61501, statement, 0);
                    break;
                case "FTRV":
                    if (Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.fv_register_direct) && tokens[0].raw_string.ToUpperInvariant() == "XMTRX")
                        return Program.generate_fv_register((ushort)61949, statement, 1);
                    break;
                case "JMP":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)16427, statement, 0);
                    break;
                case "JSR":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)16395, statement, 0);
                    break;
                case "LDC":
                case "LDC.L":
                    ushort num1 = 14;
                    if (statement.instruction == "LDC.L")
                    {
                        if (statement.tokens.Count >= 1 && statement.tokens[0].parse_type != Program.ParseType.register_indirect_post_increment)
                            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "for LDC.L, argument \"" + tokens[0].raw_string + "\" is not a register indirect post increment (example: @R0+) like expected.");
                        num1 = (ushort)7;
                    }
                    else if (statement.tokens.Count >= 1 && statement.tokens[0].parse_type != Program.ParseType.register_direct)
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "for LDC, argument \"" + tokens[0].raw_string + "\" is not a register direct (example: R0) like expected.");
                    if (!Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register))
                    {
                        if (!Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register))
                        {
                            if (!Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.gbr_register))
                            {
                                if (!Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.gbr_register))
                                {
                                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.r_bank_register_direct))
                                        return Program.generate_register_register((ushort)16526, statement);
                                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.r_bank_register_direct))
                                        return Program.generate_register_register((ushort)16519, statement);
                                    break;
                                }
                            }
                        }
                    }
                    int num2 = 0;
                    switch (tokens[1].raw_string.ToUpperInvariant())
                    {
                        case "SR":
                            num2 = 0;
                            break;
                        case "GBR":
                            num2 = 1;
                            break;
                        case "VBR":
                            num2 = 2;
                            break;
                        case "SSR":
                            num2 = 3;
                            break;
                        case "SPC":
                            num2 = 4;
                            break;
                        case "DBR":
                            num2 = 15;
                            num1 = !(statement.instruction == "LDC.L") ? (ushort)10 : (ushort)6;
                            break;
                        default:
                            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "invalid " + statement.instruction + " special register argument " + tokens[1].raw_string);
                            break;
                    }
                    return Program.generate_register((ushort)((uint)(16384 | num2 << 4) | (uint)num1), statement, 0);
                case "LDS":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)16474, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPSCR")
                        return Program.generate_register((ushort)16490, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "MACH")
                        return Program.generate_register((ushort)16394, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "MACL")
                        return Program.generate_register((ushort)16410, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "PR")
                        return Program.generate_register((ushort)16426, statement, 0);
                    break;
                case "LDS.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPUL")
                        return Program.generate_register((ushort)16470, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "FPSCR")
                        return Program.generate_register((ushort)16486, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "MACH")
                        return Program.generate_register((ushort)16390, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "MACL")
                        return Program.generate_register((ushort)16406, statement, 0);
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.other_register) && tokens[1].raw_string.ToUpperInvariant() == "PR")
                        return Program.generate_register((ushort)16422, statement, 0);
                    break;
                case "LDTLB":
                    if (Program.check_arguments(statement))
                        return 56;
                    break;
                case "MAC":
                case "MAC.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.register_indirect_post_increment))
                        return Program.generate_register_register_swapped((ushort)16399, statement);
                    break;
                case "MAC.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.register_indirect_post_increment))
                        return Program.generate_register_register_swapped((ushort)15, statement);
                    break;
                case "MOV":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24579, statement);
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                        return Program.generate_immediate_register((ushort)14, statement);
                    break;
                case "MOV.B":
                    return Program.generate_mov(statement, (ushort)0);
                case "MOV.L":
                    return Program.generate_mov(statement, (ushort)2);
                case "MOV.W":
                    return Program.generate_mov(statement, (ushort)1);
                case "MOVA":
                    if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_pc_displacement8((ushort)199, statement, 4);
                    }
                    break;
                case "MOVCA.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_indirect))
                    {
                        Program.check_error_require_register_zero(statement, 0);
                        return Program.generate_register((ushort)195, statement, 1);
                    }
                    break;
                case "MOVT":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)41, statement, 0);
                    break;
                case "MUL.L":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)7, statement);
                    break;
                case "MULS":
                case "MULS.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8207, statement);
                    break;
                case "MULU":
                case "MULU.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8206, statement);
                    break;
                case "NEG":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24587, statement);
                    break;
                case "NEGC":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24586, statement);
                    break;
                case "NOP":
                    if (Program.check_arguments(statement))
                        return 9;
                    break;
                case "NOT":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24583, statement);
                    break;
                case "OCBI":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)147, statement, 0);
                    break;
                case "OCBP":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)163, statement, 0);
                    break;
                case "OCBWB":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)179, statement, 0);
                    break;
                case "OR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8203, statement);
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)203, statement);
                    }
                    break;
                case "OR.B":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.gbr_indirect_indexed))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)207, statement);
                    }
                    break;
                case "PREF":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)131, statement, 0);
                    break;
                case "ROTCL":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16420, statement, 0);
                    break;
                case "ROTCR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16421, statement, 0);
                    break;
                case "ROTL":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16388, statement, 0);
                    break;
                case "ROTR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16389, statement, 0);
                    break;
                case "RTE":
                    if (Program.check_arguments(statement))
                        return 43;
                    break;
                case "RTS":
                    if (Program.check_arguments(statement))
                        return 11;
                    break;
                case "SETS":
                    if (Program.check_arguments(statement))
                        return 88;
                    break;
                case "SETT":
                    if (Program.check_arguments(statement))
                        return 24;
                    break;
                case "SHAD":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)16396, statement);
                    break;
                case "SHAL":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16416, statement, 0);
                    break;
                case "SHAR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16417, statement, 0);
                    break;
                case "SHLD":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)16397, statement);
                    break;
                case "SHLL":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16384, statement, 0);
                    break;
                case "SHLL16":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16424, statement, 0);
                    break;
                case "SHLL2":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16392, statement, 0);
                    break;
                case "SHLL8":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16408, statement, 0);
                    break;
                case "SHLR":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16385, statement, 0);
                    break;
                case "SHLR16":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16425, statement, 0);
                    break;
                case "SHLR2":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16393, statement, 0);
                    break;
                case "SHLR8":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct))
                        return Program.generate_register((ushort)16409, statement, 0);
                    break;
                case "SLEEP":
                    if (Program.check_arguments(statement))
                        return 27;
                    break;
                case "STC":
                    return Program.generate_stc(statement, false);
                case "STC.L":
                    return Program.generate_stc(statement, true);
                case "STS":
                    return Program.generate_sts(statement, false);
                case "STS.L":
                    return Program.generate_sts(statement, true);
                case "SUB":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12296, statement);
                    break;
                case "SUBC":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12298, statement);
                    break;
                case "SUBV":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)12299, statement);
                    break;
                case "SWAP.B":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24584, statement);
                    break;
                case "SWAP.W":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)24585, statement);
                    break;
                case "TAS.B":
                    if (Program.check_arguments(statement, Program.ParseType.register_indirect))
                        return Program.generate_register((ushort)16411, statement, 0);
                    break;
                case "TRAPA":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number))
                        return Program.generate_immediate8((ushort)195, statement);
                    break;
                case "TST":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)200, statement);
                    }
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8200, statement);
                    break;
                case "TST.B":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.gbr_indirect_indexed))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)204, statement);
                    }
                    break;
                case "XOR":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.register_direct))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)202, statement);
                    }
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8202, statement);
                    break;
                case "XOR.B":
                    if (Program.check_arguments(statement, Program.ParseType.integer_number, Program.ParseType.gbr_indirect_indexed))
                    {
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_immediate8((ushort)206, statement);
                    }
                    break;
                case "XTRCT":
                    if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)8205, statement);
                    break;
                default:
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "unknown instruction: " + statement.instruction);
                    break;
            }
            Program.error_wrong_args(statement);
            throw new Exception("code should never reach here, but compiler complains if we don't have this");
        }

        private static ushort generate_sts(Program.Statement statement, bool b_l)
        {
            if (b_l)
            {
                if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.register_indirect_pre_decrement))
                    Program.error_wrong_args(statement);
            }
            else if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.register_direct))
                Program.error_wrong_args(statement);
            int insn = 8;
            if (b_l)
                insn = 16384;
            switch (statement.tokens[0].raw_string.ToUpperInvariant())
            {
                case "MACH":
                    insn |= 2;
                    break;
                case "MACL":
                    insn |= 18;
                    break;
                case "PR":
                    insn |= 34;
                    break;
                case "FPUL":
                    insn |= 82;
                    break;
                case "FPSCR":
                    insn |= 98;
                    break;
                default:
                    Program.error_wrong_args(statement);
                    break;
            }
            return Program.generate_register((ushort)insn, statement, 1);
        }

        private static ushort generate_stc(Program.Statement statement, bool b_l)
        {
            if (statement.tokens.Count != 2)
                Program.error_wrong_args(statement);
            if (b_l)
            {
                if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.register_indirect_pre_decrement))
                {
                    if (!Program.check_arguments(statement, Program.ParseType.gbr_register, Program.ParseType.register_indirect_pre_decrement))
                    {
                        if (Program.check_arguments(statement, Program.ParseType.r_bank_register_direct, Program.ParseType.register_indirect_pre_decrement))
                            return Program.generate_register_register_swapped((ushort)16515, statement);
                        goto label_31;
                    }
                }
                int num1 = 0;
                switch (statement.tokens[0].raw_string.ToUpperInvariant())
                {
                    case "DBR":
                        num1 = 242;
                        break;
                    case "GBR":
                        num1 = 19;
                        break;
                    case "SGR":
                        num1 = 50;
                        break;
                    case "SPC":
                        num1 = 67;
                        break;
                    case "SR":
                        num1 = 3;
                        break;
                    case "SSR":
                        num1 = 51;
                        break;
                    case "VBR":
                        num1 = 35;
                        break;
                    default:
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "invalid " + statement.instruction + " special register argument " + statement.tokens[0].raw_string);
                        break;
                }
                int num2 = (int)statement.tokens[1].value;
                return (ushort)(num1 | num2 << 8 | 16384);
            }
            if (!Program.check_arguments(statement, Program.ParseType.other_register, Program.ParseType.register_direct))
            {
                if (!Program.check_arguments(statement, Program.ParseType.gbr_register, Program.ParseType.register_direct))
                {
                    if (Program.check_arguments(statement, Program.ParseType.r_bank_register_direct, Program.ParseType.register_direct))
                        return Program.generate_register_register_swapped((ushort)130, statement);
                    goto label_31;
                }
            }
            int num3 = 0;
            switch (statement.tokens[0].raw_string.ToUpperInvariant())
            {
                case "DBR":
                    num3 = 250;
                    break;
                case "GBR":
                    num3 = 18;
                    break;
                case "SGR":
                    num3 = 58;
                    break;
                case "SPC":
                    num3 = 66;
                    break;
                case "SR":
                    num3 = 2;
                    break;
                case "SSR":
                    num3 = 50;
                    break;
                case "VBR":
                    num3 = 34;
                    break;
                default:
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "invalid " + statement.instruction + " special register argument " + statement.tokens[0].raw_string);
                    break;
            }
            int num4 = (int)statement.tokens[1].value;
            return (ushort)(num3 | num4 << 8);
        label_31:
            Program.error_wrong_args(statement);
            throw new Exception("code should never reach here, but compiler complains if we don't have this");
        }

        private static ushort generate_mov(Program.Statement statement, ushort size)
        {
            if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_indirect_displacement))
            {
                switch (size)
                {
                    case 0:
                        Program.check_error_require_register_zero(statement, 0);
                        return Program.generate_displacement4_register((ushort)128, statement, 1, 1, (int)statement.tokens[1].inner_token2.value);
                    case 1:
                        Program.check_error_require_register_zero(statement, 0);
                        return Program.generate_displacement4_register((ushort)129, statement, 2, 1, (int)statement.tokens[1].inner_token2.value);
                    case 2:
                        return Program.generate_displacement4_register_register((ushort)1, statement, 4, true);
                    default:
                        throw new Exception("weird size " + (object)size + " in generate_mov, serious error");
                }
            }
            else if (Program.check_arguments(statement, Program.ParseType.register_indirect_displacement, Program.ParseType.register_direct))
            {
                switch (size)
                {
                    case 0:
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_displacement4_register((ushort)132, statement, 1, 0, (int)statement.tokens[0].inner_token2.value);
                    case 1:
                        Program.check_error_require_register_zero(statement, 1);
                        return Program.generate_displacement4_register((ushort)133, statement, 2, 0, (int)statement.tokens[0].inner_token2.value);
                    case 2:
                        return Program.generate_displacement4_register_register((ushort)5, statement, 4);
                    default:
                        throw new Exception("weird size " + (object)size + " in generate_mov, serious error");
                }
            }
            else
            {
                int size1 = 1;
                switch (size)
                {
                    case 1:
                        size1 = 2;
                        if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct))
                            return Program.generate_displacement8_register((ushort)9, statement, size1);
                        break;
                    case 2:
                        size1 = 4;
                        if (Program.check_arguments(statement, Program.ParseType.pc_displacement, Program.ParseType.register_direct))
                            return Program.generate_displacement8_register((ushort)13, statement, size1);
                        break;
                }
                if (Program.check_arguments(statement, Program.ParseType.gbr_indirect_displacement, Program.ParseType.register_direct))
                    return Program.generate_gbr_displacement((ushort)(196U | (uint)size), statement, size1, 0);
                if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.gbr_indirect_displacement))
                    return Program.generate_gbr_displacement((ushort)(192U | (uint)size), statement, size1, 1);
                int insn = (int)size;
                if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_indirect))
                    insn |= 8192;
                else if (Program.check_arguments(statement, Program.ParseType.register_indirect, Program.ParseType.register_direct))
                    insn |= 24576;
                else if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_indirect_pre_decrement))
                    insn |= 8196;
                else if (Program.check_arguments(statement, Program.ParseType.register_indirect_post_increment, Program.ParseType.register_direct))
                    insn |= 24580;
                else if (Program.check_arguments(statement, Program.ParseType.register_direct, Program.ParseType.register_indexed_indirect))
                    insn |= 4;
                else if (Program.check_arguments(statement, Program.ParseType.register_indexed_indirect, Program.ParseType.register_direct))
                    insn |= 12;
                else
                    Program.error_wrong_args(statement);
                return Program.generate_register_register_swapped((ushort)insn, statement);
            }
        }

        private static void check_error_require_register_zero(
          Program.Statement statement,
          int index,
          string register_name = "R0")
        {
            if (statement.tokens[index].value == 0L)
                return;
            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "register argument must be " + register_name + " for this form of " + statement.instruction + ", was \"" + statement.tokens[index].raw_string + "\" instead.");
        }

        private static void error_wrong_args(Program.Statement statement) => Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "wrong arguments for instruction: " + statement.instruction);

        private static bool check_arguments(
          Program.Statement statement,
          params Program.ParseType[] expected_args)
        {
            int length = expected_args.Length;
            if (statement.tokens.Count != length)
                return false;
            for (int index = 0; index < length; ++index)
            {
                Program.ParseType expectedArg = expected_args[index];
                Program.ParseType parseType = statement.tokens[index].parse_type;
                if (expectedArg != parseType && (expectedArg != Program.ParseType.integer_number || parseType != Program.ParseType.hex_number))
                {
                    if (expectedArg != Program.ParseType.integer_number || parseType != Program.ParseType.name)
                        return false;
                    string str = statement.tokens[index].raw_string.ToUpperInvariant();
                    if (!str.Contains<char>('.'))
                        str = statement.module + "." + str;
                    if (Program.symbol_table.ContainsKey(str))
                    {
                        if (Program.symbol_table[str].symbol_type != Program.SymbolType.label && Program.symbol_table[str].symbol_type != Program.SymbolType.from_symbol_directive)
                            return false;
                    }
                    else
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "can't figure out what \"" + statement.tokens[index].raw_string + "\" is, did you forget to define it, or make a typo?");
                }
            }
            return true;
        }

        private static ushort generate_register(ushort insn, Program.Statement statement, int index)
        {
            ushort num = (ushort)statement.tokens[index].value;
            return (ushort)((uint)insn | (uint)num << 8);
        }

        private static ushort generate_fv_register(ushort insn, Program.Statement statement, int index)
        {
            ushort num = (ushort)statement.tokens[index].value;
            return (ushort)((uint)insn | (uint)num << 10);
        }

        private static ushort generate_immediate_register(ushort insn, Program.Statement statement)
        {
            ushort num1 = (ushort)((ulong)statement.tokens[0].value & (ulong)byte.MaxValue);
            ushort num2 = (ushort)statement.tokens[1].value;
            return (ushort)((uint)((int)insn << 12 | (int)num2 << 8) | (uint)num1);
        }

        private static ushort generate_immediate8(ushort insn, Program.Statement statement)
        {
            ushort num = (ushort)((ulong)statement.tokens[0].value & (ulong)byte.MaxValue);
            if (num > (ushort)byte.MaxValue)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "argument \"" + (object)num + "\"  for " + statement.instruction + " too big, must be less than 256");
            return (ushort)((uint)insn << 8 | (uint)num);
        }

        private static int calculate_general_displacement(
          Program.Statement statement,
          int size,
          int max,
          int displacement_index)
        {
            int num = (int)statement.tokens[displacement_index].value;
            //Console.WriteLine("caclulate_general_displacement");
            if (num % size != 0)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)num + "\"  for " + statement.instruction + " must be " + (object)size + "-aligned (add or remove a single byte #data padding somewhere probably?)");
            int generalDisplacement = num / size;
            max *= size;
            if (generalDisplacement > max)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)(short)statement.tokens[displacement_index].value + "\"  for " + statement.instruction + " too far, can only be closer than +" + (object)max + " bytes away");
            return generalDisplacement;
        }

        private static ushort generate_displacement4_register(
          ushort insn,
          Program.Statement statement,
          int size,
          int displacement_index,
          int register)
        {
            int generalDisplacement = Program.calculate_general_displacement(statement, size, 15, displacement_index);
            return (ushort)((uint)((int)insn << 8 | register << 4) | (uint)(ushort)generalDisplacement);
        }

        private static ushort generate_displacement4_register_register(
          ushort insn,
          Program.Statement statement,
          int size,
          bool bSwap = false)
        {
            int num1 = 0;
            int index = 1;
            if (bSwap)
            {
                index = 0;
                num1 = 1;
            }
            int num2 = (int)statement.tokens[index].value;
            int generalDisplacement = Program.calculate_general_displacement(statement, size, 15, num1);
            if (statement.tokens[num1].inner_token2 == null)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument " + statement.tokens[num1].raw_string + " is missing inner register token");
            int num3 = (int)statement.tokens[num1].inner_token2.value;
            if (bSwap)
            {
                int num4 = num2;
                num2 = num3;
                num3 = num4;
            }
            return (ushort)((uint)((int)insn << 12 | num2 << 8 | num3 << 4) | (uint)(ushort)generalDisplacement);
        }

        private static ushort generate_gbr_displacement(
          ushort insn,
          Program.Statement statement,
          int size,
          int token_offset)
        {
            int num1 = (int)statement.tokens[token_offset].value;
            if (statement.tokens[token_offset].parse_type == Program.ParseType.name)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "GBR has to be displaced by number, not displacement argument \"" + statement.tokens[token_offset].raw_string + "\"");
            if (num1 % size != 0)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)num1 + "\"  for " + statement.instruction + " must be " + (object)size + "-aligned (add or remove #data padding somewhere probably?)");
            int num2 = num1 / size;
            int num3 = (int)byte.MaxValue * size;
            if (num2 > num3)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)(short)statement.tokens[token_offset].value + "\"  for " + statement.instruction + " too far, can only be closer than " + (object)num3 + " bytes away");
            return (ushort)((uint)insn << 8 | (uint)(ushort)num2);
        }

        private static int calculate_pc_displacement(
          Program.Statement statement,
          int size,
          int min_base,
          int max_base)
        {
            long pcDisplacement;
            int num1 = 0;

            int num5 = max_base;
            int num6 = min_base;
            if (statement.tokens[0].parse_type == Program.ParseType.name || statement.tokens[0].inner_token != null && statement.tokens[0].inner_token.parse_type == Program.ParseType.name)
            {

                //if (statement.instruction == "BSR")
                //    Console.WriteLine(statement.raw_line);
                long address = (long)statement.address;
                long num2 = (long)(uint)statement.tokens[0].value;
                if (size == 4)
                {
                    //    if (statement.instruction == "BSR")
                    //    Console.WriteLine("size is 4");
                    pcDisplacement = (num2 - address - 2L & 0xFFFFFFFC) / (long)size;
                    num1 = 0;
                }
                else
                {
                    //    if (statement.instruction == "BSR")
                    //        Console.WriteLine("size not 4");


                    num1 = (int)((num2 - address - 4L) % (long)size);
                    pcDisplacement = (int)((num2 - address - 4L) / (long)size);
                    if (num2 > address)
                    {
                        if (pcDisplacement > max_base)
                        {
                            num5 *= size;
                            num6 *= size;
                            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, " [i] Displacement argument \"" + (object)(short)statement.tokens[0].value + "\"  for " + statement.instruction + " too far, can only be between " + (object)num6 + " or +" + (object)num5 + " bytes away.\n [i] Calculated displacement: " + (object)pcDisplacement + "\n [i] Max forward distance: " + (object)max_base);

                        }
                    }
                    else if (address > num2)
                    {
                        if (pcDisplacement < min_base)
                        {
                            num5 *= size;
                            num6 *= size;
                            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, " [i] Displacement argument \"" + (object)(short)statement.tokens[0].value + "\"  for " + statement.instruction + " too far, can only be between " + (object)num6 + " or +" + (object)num5 + " bytes away.\n [i] Calculated displacement: " + (object)pcDisplacement + "\n [i] Max backward distance: " + (object)min_base);

                        }
                    }

                }
            }
            else if (statement.tokens[0].parse_type == Program.ParseType.absolute_displacement_address)
            {
                //Console.WriteLine("second");
                long address = (long)statement.address;
                long num3 = (long)(uint)statement.tokens[0].value;
                if (size == 4)
                {
                    //Console.WriteLine("2nd size 4");
                    pcDisplacement = (num3 - address - 2L & 0xFFFFFFFC) / (long)size;
                    num1 = 0;
                }
                else
                {
                    //Console.WriteLine("2nd size not 4");
                    num1 = (int)(num3 - address - 4L) % size;
                    pcDisplacement = (num3 - address - 4L) / (long)size;
                }
            }
            else
            {
                // Console.WriteLine("else");
                long num4 = statement.tokens[0].value;
                num1 = (int)num4 % size;
                pcDisplacement = size != 4 ? num4 / (long)size : (num4 & 0xFFFFFFFC) / (long)size;

            }
            //if (statement.instruction == "BSR")
            //    Console.WriteLine("[i] calculate_pc_displacement - pcDisplacement is: " + pcDisplacement + "\n [i] Max forward distance: " + (object)max_base + "\n [i] Max backward distance: " + (object)min_base);
 
            num5 = max_base;
            num6 = min_base;
            if (num1 != 0)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)pcDisplacement + "\"  for " + statement.instruction + " must be " + (object)size + "-aligned (add or remove #data padding somewhere probably?)");

            if (pcDisplacement > (long)num5 || pcDisplacement < (long)num6)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "displacement argument \"" + (object)(short)statement.tokens[0].value + "\"  for " + statement.instruction + " too far, can only be between " + (object)num6 + " or +" + (object)num5 + " bytes away");
            return (int)pcDisplacement;
        }

        private static ushort generate_pc_displacement8(
          ushort insn,
          Program.Statement statement,
          int size = 2)
        {
            short pcDisplacement;
            //Console.WriteLine("generate_pc_displacement8");
            if (statement.instruction == "MOV.W")
                pcDisplacement = (short)Program.calculate_pc_displacement(statement, size, 0, 255); //-256 and 254 are wrong, mixing instr/displ with bytes?
            else if (statement.instruction == "MOV.L" || statement.instruction == "MOVA")
                pcDisplacement = (short)Program.calculate_pc_displacement(statement, size, 0, 255); //-256 and 254 are wrong, mixing instr/displ with bytes?
            else
                pcDisplacement = (short)Program.calculate_pc_displacement(statement, size, -128, 127); //-256 and 254 are wrong, mixing instr/displ with bytes?
            return (ushort)((int)insn << 8 | (int)(ushort)pcDisplacement & (int)byte.MaxValue);
        }

        private static ushort generate_displacement8_register(
          ushort insn,
          Program.Statement statement,
          int size = 2)
        {
            ushort num = (ushort)statement.tokens[1].value;
            return Program.generate_pc_displacement8((ushort)((uint)insn << 4 | (uint)num), statement, size);
        }

        private static ushort generate_pc_displacement12(
          ushort insn,
          Program.Statement statement,
          int size = 2)
        {
            int pcDisplacement = Program.calculate_pc_displacement(statement, size, -2048, 2047);
            // short instruction_op_code = (ushort)((int)(insn << 12) | (int)(pcDisplacement & 0x0FFF));
            return (ushort)((int)(insn << 12) | (int)(pcDisplacement & 0x0FFF));
        }

        private static ushort generate_fv_register_register(ushort insn, Program.Statement statement)
        {
            ushort num1 = (ushort)statement.tokens[0].value;
            ushort num2 = (ushort)statement.tokens[1].value;
            return (ushort)((int)insn | (int)num1 << 8 | (int)num2 << 10);
        }

        private static ushort generate_register_register_swapped(
          ushort insn,
          Program.Statement statement,
          int offset = 0)
        {
            ushort num1 = (ushort)statement.tokens[offset].value;
            ushort num2 = (ushort)statement.tokens[offset + 1].value;
            return (ushort)((int)insn | (int)num1 << 4 | (int)num2 << 8);
        }

        private static ushort generate_register_register(
          ushort insn,
          Program.Statement statement,
          int offset = 0)
        {
            ushort num1 = (ushort)statement.tokens[offset].value;
            ushort num2 = (ushort)statement.tokens[offset + 1].value;
            return (ushort)((int)insn | (int)num1 << 8 | (int)num2 << 4);
        }

        private static void intermediate_step(List<Program.Statement> statements)
        {
            uint startingOffset = Program.starting_offset;
            int num = 1;
            foreach (Program.Statement statement in statements)
            {
                if (statement.repeat_count <= 0)
                    statement.repeat_count = 1;
                statement.repeat_count *= num;
                num = 1;
                if (statement.instruction == "#DATA" || statement.instruction == "#DATA16" || statement.instruction == "#DATA8")
                {
                    for (int index = 0; index < statement.repeat_count; ++index)
                        Program.process_data(statement, ref startingOffset);
                }
                else if (statement.instruction == "#ALIGN4" || statement.instruction == "#ALIGN4_NOP")
                {
                    if (statement.repeat_count > 1)
                    {
                        statement.repeat_count = 1;
                        Program.Warn(statement.raw_line, statement.module, statement.line_number, -1, "repeat has been applied to alignment directive");
                    }
                    statement.address = startingOffset;
                    while (startingOffset % 4U != 0U)
                        ++startingOffset;
                }
                // Figure out the offset shift for the pad values //
                else if (statement.instruction == "#PAD_TO" || statement.instruction == "#NOP_TO")
                {
                    if (statement.repeat_count > 1)
                    {
                        statement.repeat_count = 1;
                        Program.Warn(statement.raw_line, statement.module, statement.line_number, -1, "repeat has been applied to #PAD_TO/#NOP_TO directive");
                    }
                    statement.address = startingOffset;

                    // Determine the value of the tokens //
                    Program.assign_value_to_token(statement, statement.tokens[0]);
                    uint target = (uint)statement.tokens[0].value;

                    while (startingOffset < target)
                        ++startingOffset;
                }
                else if (statement.instruction == "#ALIGN16" || statement.instruction == "#ALIGN16_NOP")
                {
                    if (statement.repeat_count > 1)
                    {
                        statement.repeat_count = 1;
                        Program.Warn(statement.raw_line, statement.module, statement.line_number, -1, "repeat has been applied to alignment directive");
                    }
                    statement.address = startingOffset;
                    while (startingOffset % 16U != 0U)
                        ++startingOffset;
                }
                else if (statement.instruction == "#ALIGN")
                {
                    if (statement.repeat_count > 1)
                    {
                        statement.repeat_count = 1;
                        Program.Warn(statement.raw_line, statement.module, statement.line_number, -1, "repeat has been applied to alignment directive");
                    }
                    statement.address = startingOffset;
                    while (startingOffset % 2U != 0U)
                        ++startingOffset;
                }
                else if (statement.instruction == "#REPEAT")
                {
                    if (statement.tokens.Count == 0)
                        num *= 2;
                    else if (statement.tokens.Count == 1)
                    {
                        Program.assign_value_to_token(statement, statement.tokens[0]);
                        num *= (int)statement.tokens[0].value;
                    }
                    else
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "too many parameters to #repeat directive");
                }
                else if (statement.instruction == "#IMPORT_RAW_DATA")
                    Program.process_import_raw_data(statement, ref startingOffset);
                else if (!(statement.instruction == "#LITTLE_ENDIAN") && !(statement.instruction == "#BIG_ENDIAN") && statement.instruction != "#SYMBOL")
                {
                    for (int index = 0; index < statement.repeat_count; ++index)
                        Program.process_statement(statement, ref startingOffset);
                }
            }
            foreach (KeyValuePair<string, Program.Symbol> keyValuePair in Program.symbol_table)
            {
                Program.Symbol symbol = keyValuePair.Value;
                if (symbol.symbol_type == Program.SymbolType.label)
                {
                    int statementNumber = symbol.statement_number;
                    if (statementNumber == statements.Count)
                    {
                        symbol.address = startingOffset;
                        symbol.value = (long)symbol.address;
                    }
                    else
                    {
                        uint address = statements[statementNumber].address;
                        while (statementNumber < statements.Count - 1 && (statements[statementNumber].instruction == "#ALIGN4" || statements[statementNumber].instruction == "#ALIGN4_NOP" || statements[statementNumber].instruction == "#ALIGN16" || statements[statementNumber].instruction == "#ALIGN16_NOP" || statements[statementNumber].instruction == "#ALIGN" || statements[statementNumber].instruction == "#PAD_TO" || statements[statementNumber].instruction == "#NOP_TO"))
                        {
                            ++statementNumber;
                            address = statements[statementNumber].address;
                        }
                        if (statementNumber != symbol.statement_number && (statements[statementNumber].instruction == "#ALIGN4" || statements[statementNumber].instruction == "#ALIGN4_NOP" || statements[statementNumber].instruction == "#ALIGN16" || statements[statementNumber].instruction == "#ALIGN16_NOP" || statements[statementNumber].instruction == "#ALIGN" || statements[statementNumber].instruction == "#PAD_TO" || statements[statementNumber].instruction == "#NOP_TO"))
                        {
                            symbol.statement_number = statementNumber;
                            symbol.address = address;
                        }
                        else
                            symbol.address = statements[symbol.statement_number].address;
                        symbol.value = (long)symbol.address;
                    }
                }
            }
            bool flag = false;
            foreach (Program.Statement statement in statements)
            {
                foreach (Program.Token token in statement.tokens)
                {

                    try
                    {
                        Program.assign_value_to_token(statement, token);
                    }
                    catch (Exception)
                    {
                        flag = true;
                    }
                }
            }
            if (flag)
                throw new Exception("NO ERROR HANDLING YET SORRY");
        }

        private static void assign_value_to_token(Program.Statement statement, Program.Token token)
        {
            if (token.is_value_assigned)
                return;
            uint val2 = 4;
            if (statement.instruction == "#DATA8")
                val2 = 1U;
            else if (statement.instruction == "#DATA16")
                val2 = 2U;
            switch (token.parse_type)
            {
                case Program.ParseType.name:
                    Program.Symbol symbol = Program.resolve_name(statement, token.raw_string);
                    token.value = symbol.value;
                    token.size = Math.Min(symbol.size, val2);
                    token.is_value_assigned = true;
                    break;
                case Program.ParseType.integer_number:
                    token.is_value_assigned = true;
                    token.value = long.Parse(token.raw_string, (IFormatProvider)CultureInfo.InvariantCulture);
                    token.size = val2;
                    break;
                case Program.ParseType.float_number:
                    token.is_value_assigned = true;
                    token.value = (long)BitConverter.ToUInt32(BitConverter.GetBytes(float.Parse(token.raw_string, (IFormatProvider)CultureInfo.InvariantCulture)), 0);
                    token.size = val2;
                    if (val2 >= 4U)
                        break;
                    token.value >>= (4 - (int)val2) * 8;
                    break;
                case Program.ParseType.hex_number:
                case Program.ParseType.absolute_displacement_address:
                    token.is_value_assigned = true;
                    token.value = Convert.ToInt64(token.raw_string, 16);
                    token.size = (uint)(token.raw_string.Length - 2) / 2U;
                    if (val2 == 4U || token.size <= val2)
                        break;
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "hex number " + token.raw_string + " is larger than " + (object)val2 + " bytes");
                    break;
                case Program.ParseType.register_indexed_indirect:
                    if (token.inner_token == null || token.inner_token2 == null)
                    {
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "error finding inner token of indirect addressing token \"" + token.raw_string + "\", this is probably an assembler error on our part? please let us know.");
                        break;
                    }
                    if (token.inner_token2 == null)
                        break;
                    Program.assign_value_to_token(statement, token.inner_token);
                    if (token.inner_token.value != 0L)
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "first register in indirect addressing must be R0, was  \"" + token.inner_token.raw_string + "\" instead");
                    Program.assign_value_to_token(statement, token.inner_token2);
                    token.value = token.inner_token2.value;
                    token.is_value_assigned = true;
                    break;
                case Program.ParseType.string_data:
                    token.is_value_assigned = true;
                    token.value = 0L;
                    token.size = (uint)token.raw_string.Length;
                    if (val2 == 4U || (int)val2 == (int)token.size)
                        break;
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "string \"" + token.raw_string + "\" is not exactly " + (object)val2 + " bytes long");
                    break;
                case Program.ParseType.expression:
                    Program.assign_value_to_expression(statement, token);
                    token.size = val2;
                    break;
                default:
                    if (token.inner_token == null)
                        break;
                    Program.assign_value_to_token(statement, token.inner_token);
                    token.is_value_assigned = true;
                    token.value = token.inner_token.value;
                    break;
            }
        }

        private static Program.Symbol resolve_name(Program.Statement statement, string raw_key)
        {
            string key = raw_key.ToUpperInvariant();
            bool flag = Program.symbol_table.ContainsKey(key);
            if (!flag)
            {
                if (!key.Contains("."))
                    key = statement.module + "." + key;
                flag = Program.symbol_table.ContainsKey(key);
            }
            if (flag)
            {
                if (Program.symbol_table[key].symbol_type == Program.SymbolType.instruction)
                {
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, raw_key + " is an instruction, but is being used as an argument instead. did you forget a newline?");
                }
                else
                {
                    Program.Symbol symbol = Program.symbol_table[key];
                    if (symbol.has_been_associated)
                        return symbol;
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "value of '" + raw_key + "' can't be resolved. probably can't use a label here");
                }
            }
            else
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "i don't know what " + raw_key + " is, did you forget to define it, or make a typo?");
            return (Program.Symbol)null;
        }

        private static void assign_value_to_expression(Program.Statement statement, Program.Token token)
        {
            int count = token.expression.subtokens_type.Count;
            int num = 0;
            for (int index = 0; index < count; ++index)
            {
                if (token.expression.subtokens_type[index] == Program.Expression.SubtokenType.open_parenthesis)
                    ++num;
                else if (token.expression.subtokens_type[index] == Program.Expression.SubtokenType.close_parenthesis)
                {
                    if (num <= 0)
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "unexpected closing ')' without corresponding '(' in expression {" + token.raw_string + "}");
                    --num;
                }
            }
            if (num > 0)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "unclosed '(', expected a corresponding ')' somewhere in expression {" + token.raw_string + "}");
            token.value = Program.calculate_expression(statement, token.expression, 0, token.expression.subtokens.Count);
        }

        private static long calculate_expression(
          Program.Statement statement,
          Program.Expression e,
          int start_index,
          int end_index)
        {
            int num1 = end_index - start_index;
            switch (num1)
            {
                case 1:
                    if (e.subtokens_type[start_index] == Program.Expression.SubtokenType.name)
                        return Program.resolve_name(statement, e.subtokens[start_index]).value;
                    if (e.subtokens_type[start_index] == Program.Expression.SubtokenType.decimal_number)
                        return long.Parse(e.subtokens[start_index], (IFormatProvider)CultureInfo.InvariantCulture);
                    if (e.subtokens_type[start_index] == Program.Expression.SubtokenType.hex_number)
                        return Convert.ToInt64(e.subtokens[start_index], 16);
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "expression processing error near " + e.subtokens[start_index] + ", expected a symbol or a number here.");
                    break;
                case 2:
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "expression processing error near '" + e.subtokens[start_index] + "' and '" + e.subtokens[end_index - 1] + "', perhaps a left or right hand side of an operation is missing?");
                    break;
                default:
                    if (e.subtokens_type[start_index] == Program.Expression.SubtokenType.open_parenthesis && e.subtokens_type[end_index - 1] == Program.Expression.SubtokenType.close_parenthesis)
                    {
                        if (num1 == 3)
                            return Program.calculate_expression(statement, e, start_index + 1, end_index - 1);
                        int num2 = 0;
                        for (int index = start_index; index < end_index - 1; ++index)
                        {
                            if (e.subtokens_type[index] == Program.Expression.SubtokenType.open_parenthesis)
                                ++num2;
                            else if (e.subtokens_type[index] == Program.Expression.SubtokenType.close_parenthesis)
                                --num2;
                            if (num2 <= 0)
                                break;
                        }
                        if (num2 == 1)
                            return Program.calculate_expression(statement, e, start_index + 1, end_index - 1);
                        break;
                    }
                    break;
            }
            int num3 = start_index;
            int num4 = Program.score_subtoken_order_of_operations(e.subtokens_type[start_index]);
            Program.Expression.SubtokenType subtokenType = e.subtokens_type[start_index];
            int num5 = 0;
            if (e.subtokens_type[start_index] == Program.Expression.SubtokenType.open_parenthesis)
                ++num5;
            for (int index = start_index + 1; index < end_index; ++index)
            {
                if (e.subtokens_type[index] == Program.Expression.SubtokenType.open_parenthesis)
                    ++num5;
                else if (e.subtokens_type[index] == Program.Expression.SubtokenType.close_parenthesis)
                    --num5;
                else if (num5 <= 0)
                {
                    int num6 = Program.score_subtoken_order_of_operations(e.subtokens_type[index]);
                    if (num6 <= num4)
                    {
                        num3 = index;
                        num4 = num6;
                        subtokenType = e.subtokens_type[index];
                    }
                }
            }
            if (num3 == start_index)
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "expression processing error near " + e.subtokens[start_index] + ", perhaps a left or right hand side of an operation is missing?");
            long expression1 = Program.calculate_expression(statement, e, start_index, num3);
            long expression2 = Program.calculate_expression(statement, e, num3 + 1, end_index);
            switch (subtokenType)
            {
                case Program.Expression.SubtokenType.add:
                    return expression1 + expression2;
                case Program.Expression.SubtokenType.subtract:
                    return expression1 - expression2;
                case Program.Expression.SubtokenType.multiply:
                    return expression1 * expression2;
                case Program.Expression.SubtokenType.divide:
                    return expression1 / expression2;
                default:
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "expression processing error near " + e.subtokens[num3] + ", i expected that to be an operator, but it doesnt seem to be?");
                    return 0;
            }
        }

        private static int score_subtoken_order_of_operations(Program.Expression.SubtokenType t)
        {
            switch (t)
            {
                case Program.Expression.SubtokenType.add:
                case Program.Expression.SubtokenType.subtract:
                    return 0;
                case Program.Expression.SubtokenType.multiply:
                case Program.Expression.SubtokenType.divide:
                    return 5;
                default:
                    return 100;
            }
        }

        private static void process_import_raw_data(
          Program.Statement statement,
          ref uint current_address)
        {
            statement.address = current_address;
            if (statement.tokens != null && statement.tokens.Count == 1)
            {
                Program.Token token = statement.tokens[0];
                if (token.parse_type == Program.ParseType.string_data)
                {
                    string str = Path.Combine(Program.working_directory, token.raw_string);
                    if (!File.Exists(str))
                        return;
                    long length = new FileInfo(str).Length;
                    if (length > (long)int.MaxValue)
                    {
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, str + " is too large, must be less than or equal to " + (object)int.MaxValue + " bytes");
                    }
                    else
                    {
                        token.size = (uint)length;
                        token.value = (long)token.size;
                        token.is_value_assigned = true;
                        current_address += (uint)length * (uint)statement.repeat_count;
                    }
                }
                else
                    Program.Error(statement.raw_line, statement.module, statement.line_number, -1, token.raw_string + " is a symbol that exists, but not of the right type to use for #data");
            }
            else
                Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "wrong number of inputs for " + statement.instruction + " directive, need a single filename");
        }

        private static void process_data(Program.Statement statement, ref uint current_address)
        {
            statement.address = current_address;
            uint num = 4;
            if (statement.instruction == "#DATA16")
                num = 2U;
            else if (statement.instruction == "#DATA8")
                num = 1U;
            foreach (Program.Token token in statement.tokens)
            {
                switch (token.parse_type)
                {
                    case Program.ParseType.name:
                        string key = token.raw_string.ToUpperInvariant();
                        if (!key.Contains("."))
                            key = statement.module + "." + key;
                        if (Program.symbol_table.ContainsKey(key))
                        {
                            Program.Symbol symbol = Program.symbol_table[key];
                            if (symbol.symbol_type == Program.SymbolType.from_symbol_directive || symbol.symbol_type == Program.SymbolType.label)
                            {
                                num = symbol.size;

                                if (statement.instruction == "#DATA16")
                                {
                                    num = 2U;
                                }
                                else if (statement.instruction == "#DATA8")
                                {
                                    num = 1U;

                                }
                                else
                                {
                                    num = symbol.size;
                                }
                                current_address += num;
                                continue;
                            }
                            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, token.raw_string + " is a symbol that exists, but not of the right type to use for #data");
                            continue;
                        }
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, token.raw_string + " is not a valid #data integer number, label, or symbol");
                        continue;
                    case Program.ParseType.integer_number:
                        current_address += num;
                        continue;
                    case Program.ParseType.float_number:
                        current_address += num;
                        continue;
                    case Program.ParseType.hex_number:
                        current_address += (uint)(token.raw_string.Length - 2) / 2U;
                        continue;
                    case Program.ParseType.string_data:
                        current_address += (uint)token.raw_string.Length;
                        continue;
                    case Program.ParseType.expression:
                        current_address += num;
                        continue;
                    default:
                        Program.Error(statement.raw_line, statement.module, statement.line_number, -1, token.raw_string + " is not a valid #data integer number");
                        continue;
                }
            }
            if (num >= 4U || (int)statement.address + (int)num == (int)current_address)
                return;
            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "size mismatch in " + statement.instruction + " expected " + (object)num + " bytes, found " + (object)(uint)((int)statement.address - (int)current_address) + " bytes.");
        }

        private static void process_statement(Program.Statement statement, ref uint current_address)
        {
            statement.address = current_address;
            current_address += 2U;
        }

        private static void init_symbols()
        {
            Program.symbol_table = new Dictionary<string, Program.Symbol>();
            Program.add_instruction_symbol("ADD");
            Program.add_instruction_symbol("ADDC");
            Program.add_instruction_symbol("ADDV");
            Program.add_instruction_symbol("AND");
            Program.add_instruction_symbol("BF");
            Program.add_instruction_symbol("BF/S");
            Program.add_instruction_symbol("BF.S");
            Program.add_instruction_symbol("BRA");
            Program.add_instruction_symbol("BRAF");
            Program.add_instruction_symbol("BSR");
            Program.add_instruction_symbol("BSRF");
            Program.add_instruction_symbol("BT");
            Program.add_instruction_symbol("BT/S");
            Program.add_instruction_symbol("BT.S");
            Program.add_instruction_symbol("CLRMAC");
            Program.add_instruction_symbol("CLRS");
            Program.add_instruction_symbol("CLRT");
            Program.add_instruction_symbol("CMP/EQ");
            Program.add_instruction_symbol("CMP/GE");
            Program.add_instruction_symbol("CMP/GT");
            Program.add_instruction_symbol("CMP/HI");
            Program.add_instruction_symbol("CMP/HS");
            Program.add_instruction_symbol("CMP/PL");
            Program.add_instruction_symbol("CMP/PZ");
            Program.add_instruction_symbol("CMP/STR");
            Program.add_instruction_symbol("DIV0S");
            Program.add_instruction_symbol("DIV0U");
            Program.add_instruction_symbol("DIV1");
            Program.add_instruction_symbol("DMULS.L");
            Program.add_instruction_symbol("DMULU.L");
            Program.add_instruction_symbol("DT");
            Program.add_instruction_symbol("EXTS");
            Program.add_instruction_symbol("EXTS.B");
            Program.add_instruction_symbol("EXTS.W");
            Program.add_instruction_symbol("EXTU");
            Program.add_instruction_symbol("EXTU.B");
            Program.add_instruction_symbol("EXTU.W");
            Program.add_instruction_symbol("FABS");
            Program.add_instruction_symbol("FADD");
            Program.add_instruction_symbol("FCMP");
            Program.add_instruction_symbol("FCNVDS");
            Program.add_instruction_symbol("FCNVSD");
            Program.add_instruction_symbol("FDIV");
            Program.add_instruction_symbol("FIPR");
            Program.add_instruction_symbol("FLDI0");
            Program.add_instruction_symbol("FLDI1");
            Program.add_instruction_symbol("FLDS");
            Program.add_instruction_symbol("FLOAT");
            Program.add_instruction_symbol("FMAC");
            Program.add_instruction_symbol("FMOV");
            Program.add_instruction_symbol("FMOV.S");
            Program.add_instruction_symbol("FMOV/S");
            Program.add_instruction_symbol("FMUL");
            Program.add_instruction_symbol("FNEG");
            Program.add_instruction_symbol("FRCHG");
            Program.add_instruction_symbol("FSCHG");
            Program.add_instruction_symbol("FSQRT");
            Program.add_instruction_symbol("FSTS");
            Program.add_instruction_symbol("FSUB");
            Program.add_instruction_symbol("FTRC");
            Program.add_instruction_symbol("FTRV");
            Program.add_instruction_symbol("JMP");
            Program.add_instruction_symbol("JSR");
            Program.add_instruction_symbol("LDC");
            Program.add_instruction_symbol("LDC.L");
            Program.add_instruction_symbol("LDC/L");
            Program.add_instruction_symbol("LDS");
            Program.add_instruction_symbol("LDS.L");
            Program.add_instruction_symbol("LDS/L");
            Program.add_instruction_symbol("LDTLB");
            Program.add_instruction_symbol("MAC.L");
            Program.add_instruction_symbol("MAC.W");
            Program.add_instruction_symbol("MOV");
            Program.add_instruction_symbol("MOV.B");
            Program.add_instruction_symbol("MOV.W");
            Program.add_instruction_symbol("MOV.L");
            Program.add_instruction_symbol("MOVA");
            Program.add_instruction_symbol("MOVCA.L");
            Program.add_instruction_symbol("MOVT");
            Program.add_instruction_symbol("MUL.L");
            Program.add_instruction_symbol("MULS.W");
            Program.add_instruction_symbol("MULU.W");
            Program.add_instruction_symbol("NEG");
            Program.add_instruction_symbol("NEGC");
            Program.add_instruction_symbol("NOP");
            Program.add_instruction_symbol("NOT");
            Program.add_instruction_symbol("OCBI");
            Program.add_instruction_symbol("OCBP");
            Program.add_instruction_symbol("OCBWB");
            Program.add_instruction_symbol("OR");
            Program.add_instruction_symbol("PREF");
            Program.add_instruction_symbol("ROTCL");
            Program.add_instruction_symbol("ROTCR");
            Program.add_instruction_symbol("ROTL");
            Program.add_instruction_symbol("ROTR");
            Program.add_instruction_symbol("RTE");
            Program.add_instruction_symbol("RTS");
            Program.add_instruction_symbol("SETS");
            Program.add_instruction_symbol("SETT");
            Program.add_instruction_symbol("SHAD");
            Program.add_instruction_symbol("SHAL");
            Program.add_instruction_symbol("SHAR");
            Program.add_instruction_symbol("SHLD");
            Program.add_instruction_symbol("SHLL");
            Program.add_instruction_symbol("SHLL2");
            Program.add_instruction_symbol("SHLL8");
            Program.add_instruction_symbol("SHLL16");
            Program.add_instruction_symbol("SHLR");
            Program.add_instruction_symbol("SHLR2");
            Program.add_instruction_symbol("SHLR8");
            Program.add_instruction_symbol("SHLR16");
            Program.add_instruction_symbol("SLEEP");
            Program.add_instruction_symbol("STC");
            Program.add_instruction_symbol("STC.L");
            Program.add_instruction_symbol("STS");
            Program.add_instruction_symbol("STS.L");
            Program.add_instruction_symbol("SUB");
            Program.add_instruction_symbol("SUBC");
            Program.add_instruction_symbol("SUBV");
            Program.add_instruction_symbol("SWAP");
            Program.add_instruction_symbol("TAS");
            Program.add_instruction_symbol("TRAPA");
            Program.add_instruction_symbol("TST");
            Program.add_instruction_symbol("XOR");
            Program.add_instruction_symbol("XTRCT");
            Program.add_builtin_symbol("#DATA");
            Program.add_builtin_symbol("#DATA8");
            Program.add_builtin_symbol("#DATA16");
            Program.add_builtin_symbol_alias("#D", "#DATA");
            Program.add_builtin_symbol_alias("#D8", "#DATA8");
            Program.add_builtin_symbol_alias("#D16", "#DATA16");
            Program.add_builtin_symbol("#PAD_TO");
            Program.add_builtin_symbol("#NOP_TO");
            Program.add_builtin_symbol("#SYMBOL");
            Program.add_builtin_symbol("#REPEAT");
            Program.add_builtin_symbol("#ALIGN4");
            Program.add_builtin_symbol("#ALIGN");
            Program.add_builtin_symbol("#ALIGN4_NOP");
            Program.add_builtin_symbol("#ALIGN16");
            Program.add_builtin_symbol("#ALIGN16_NOP");
            Program.add_builtin_symbol("#LONG_STRING_DATA");
            Program.add_builtin_symbol("#IMPORT_RAW_DATA");
            Program.add_builtin_symbol("#BIG_ENDIAN");
            Program.add_builtin_symbol("#LITTLE_ENDIAN");
            for (int index = 0; index <= 7; ++index)
                Program.add_register_symbol("R" + (object)index + "_BANK", index, Program.RegisterType.r_bank);
            for (int index = 0; index <= 15; ++index)
            {
                Program.add_register_symbol("R" + (object)index, index, Program.RegisterType.r);
                Program.add_register_symbol("FR" + (object)index, index, Program.RegisterType.fr);
                if (index % 2 == 0)
                {
                    Program.add_register_symbol("XD" + (object)index, index, Program.RegisterType.xd);
                    Program.add_register_symbol("DR" + (object)index, index, Program.RegisterType.dr);
                    if (index % 4 == 0)
                        Program.add_register_symbol("FV" + (object)index, index / 4, Program.RegisterType.fv);
                }
            }
            Program.add_register_symbol("PC", register_type: Program.RegisterType.pc);
            Program.add_register_symbol("GBR", register_type: Program.RegisterType.gbr);
            Program.add_register_symbol("SR");
            Program.add_register_symbol("SSR");
            Program.add_register_symbol("SPC");
            Program.add_register_symbol("VBR");
            Program.add_register_symbol("SGR");
            Program.add_register_symbol("DBR");
            Program.add_register_symbol("MACH");
            Program.add_register_symbol("MACL");
            Program.add_register_symbol("PR");
            Program.add_register_symbol("FPSCR");
            Program.add_register_symbol("FPUL");
            Program.add_register_symbol("XMTRX");
            Program.add_register_symbol("MD");
            Program.add_register_symbol("RB");
            Program.add_register_symbol("BL");
            Program.add_register_symbol("FD");
            Program.add_register_symbol("IMASK");
            Program.add_register_symbol("M");
            Program.add_register_symbol("Q");
            Program.add_register_symbol("S");
            Program.add_register_symbol("T");
        }

        private static void add_instruction_symbol(string name)
        {
            name = name.ToUpperInvariant();
            Program.Symbol symbol = new Program.Symbol();
            symbol.symbol_type = Program.SymbolType.instruction;
            symbol.name = name;
            symbol.line_number = -1;
            symbol.statement_number = -1;
            symbol.size = 4U;
            symbol.module = "$core";
            symbol.has_been_associated = true;
            Program.symbol_table.Add(symbol.name, symbol);
        }

        private static void add_builtin_symbol_alias(string alias_name, string target_name)
        {
            alias_name = alias_name.ToUpperInvariant();
            target_name = target_name.ToUpperInvariant();
            Program.Symbol symbol = new Program.Symbol();
            symbol.symbol_type = Program.SymbolType.alias;
            symbol.alias_target = target_name;
            symbol.name = alias_name;
            symbol.line_number = -1;
            symbol.statement_number = -1;
            symbol.size = 4U;
            symbol.module = "$core";
            symbol.has_been_associated = true;
            Program.symbol_table.Add(symbol.name, symbol);
        }

        private static void add_builtin_symbol(string name)
        {
            name = name.ToUpperInvariant();
            Program.Symbol symbol = new Program.Symbol();
            symbol.symbol_type = Program.SymbolType.builtin;
            symbol.name = name;
            symbol.line_number = -1;
            symbol.statement_number = -1;
            symbol.size = 4U;
            symbol.module = "$core";
            symbol.has_been_associated = true;
            Program.symbol_table.Add(symbol.name, symbol);
        }

        private static void add_register_symbol(
          string name,
          int value = -1,
          Program.RegisterType register_type = Program.RegisterType.other)
        {
            name = name.ToUpperInvariant();
            Program.Symbol symbol = new Program.Symbol();
            symbol.symbol_type = Program.SymbolType.register;
            symbol.name = name;
            symbol.line_number = -1;
            symbol.statement_number = -1;
            symbol.value = (long)value;
            symbol.register_type = register_type;
            symbol.module = "$core";
            symbol.has_been_associated = true;
            Program.symbol_table.Add(symbol.name, symbol);
        }

        private static void add_symbol(
          string name,
          long value,
          Program.SymbolType symbol_type,
          char[] input_line,
          int line_number,
          int char_index,
          int statement_number,
          uint size,
          string module)
        {
            name = name.ToUpperInvariant();
            string key = module + "." + name;
            if (Program.symbol_table.ContainsKey(name))
            {
                if (Program.symbol_table[name].symbol_type != Program.SymbolType.builtin && Program.symbol_table[name].symbol_type != Program.SymbolType.instruction && Program.symbol_table[name].symbol_type != Program.SymbolType.register)
                    Program.Error(input_line, module, line_number, char_index, "Attempt to redeclare symbol or label \"" + key + "\" that already exists, was declared in module " + Program.symbol_table[name].module + ", line " + (object)Program.symbol_table[name].line_number);
                else
                    Program.Error(input_line, module, line_number, char_index, "Attempt to declare symbol or label \"" + name + "\", but that's already a builtin symbol, register, or instruction.");
            }
            if (Program.symbol_table.ContainsKey(key))
            {
                if (Program.symbol_table[key].symbol_type != Program.SymbolType.builtin && Program.symbol_table[key].symbol_type != Program.SymbolType.instruction && Program.symbol_table[key].symbol_type != Program.SymbolType.register)
                    Program.Error(input_line, module, line_number, char_index, "Attempt to redeclare symbol or label \"" + key + "\" that already exists, was declared in module " + Program.symbol_table[key].module + ", line " + (object)Program.symbol_table[key].line_number);
                else
                    Program.Error(input_line, module, line_number, char_index, "Attempt to declare symbol or label \"" + key + "\", but that's already a builtin symbol, register, or instruction.");
            }
            Program.Symbol symbol = new Program.Symbol();
            symbol.name = key;
            symbol.short_name = name;
            symbol.value = value;
            symbol.symbol_type = symbol_type;
            symbol.line_number = line_number;
            symbol.statement_number = statement_number;
            symbol.size = size;
            symbol.module = module;
            symbol.has_been_associated = symbol_type != Program.SymbolType.label;
            Program.symbol_table.Add(symbol.name, symbol);
        }

        private static List<Program.Statement> tokenize_and_parse(
          StreamReader reader,
          string module,
          int statement_number_offset)
        {
            List<Program.Statement> statements = new List<Program.Statement>();
            int line_number = 1;
            for (string str = reader.ReadLine(); str != null; str = reader.ReadLine())
            {
                Program.Statement statement = Program.tokenize_line(str.ToCharArray(), line_number, statements.Count, module);
                if (statement != null)
                {
                    statement.module = module;
                    if (statement.instruction == "#LONG_STRING_DATA")
                        Program.parse_multiline_string(reader, statement, ref line_number);
                    if (Program.symbol_table.ContainsKey(statement.instruction) && Program.symbol_table[statement.instruction].symbol_type == Program.SymbolType.alias)
                        statement.instruction = Program.symbol_table[statement.instruction].alias_target;
                    statements.Add(statement);
                }
                ++line_number;
            }
            Program.associate_labels(statements);
            return statements;
        }

        private static void parse_multiline_string(
          StreamReader reader,
          Program.Statement statement,
          ref int line_number)
        {
            StringBuilder stringBuilder = new StringBuilder();
            for (string str = reader.ReadLine(); str != null; str = reader.ReadLine())
            {
                if (str.StartsWith("#") && str.ToUpperInvariant().StartsWith("#END_LONG_STRING_DATA"))
                {
                    statement.tokens.Add(new Program.Token()
                    {
                        parse_type = Program.ParseType.string_data,
                        raw_string = stringBuilder.ToString()
                    });
                    statement.instruction = "#DATA";
                    ++line_number;
                    return;
                }
                stringBuilder.Append(str);
                stringBuilder.Append('\n');
                ++line_number;
            }
            Program.Error(statement.raw_line, statement.module, statement.line_number, -1, "long string data was not properly ended with #END_LONG_STRING_DATA");
        }

        private static Program.Statement tokenize_line(
          char[] input_line,
          int line_number,
          int statement_number,
          string module)
        {
            int index = 0;
            if (!Program.find_token(input_line, line_number, statement_number, ref index))
                return (Program.Statement)null;
            Program.Statement output = new Program.Statement();
            output.tokens = new List<Program.Token>();
            output.line_number = line_number;
            output.repeat_count = 1;
            Program.Token token = Program.ReadSymbolOrLabel(input_line, line_number, statement_number, ref index, module);
            while (token != null && token.parse_type == Program.ParseType.label_declaration && index < input_line.Length)
                token = Program.ReadSymbolOrLabel(input_line, line_number, statement_number, ref index, module);
            if (token == null || token.parse_type == Program.ParseType.label_declaration)
                return (Program.Statement)null;
            output.instruction = token.raw_string.ToUpperInvariant();
            while (Program.find_token(input_line, line_number, statement_number, ref index))
                output.tokens.Add(Program.ReadArgument(input_line, line_number, statement_number, ref index, module));
            output.raw_line = input_line;

            // #IF check //
            if (output.instruction == "#IF")
            {
                if (Program.if_status != Program.IfStatus.not_in_if_block)
                {
                    Program.Error(input_line, module, line_number, -1, output.instruction + " statement unexepcted at this time!  Nested IF statements are not supported!");
                }

                // Sets the if_status value based on the label //
                if (Program.CheckIfStatement(output, input_line, line_number, module))
                    Program.if_status = Program.IfStatus.in_false_if_statement;
                else
                    Program.if_status = Program.IfStatus.in_true_if_statement;

                return (Program.Statement)null;
            }

            // #ELSE_IF check //
            if (output.instruction == "#ELSE_IF")
            {
                switch (Program.if_status)
                {
                    case Program.IfStatus.not_in_if_block:
                        Program.Error(input_line, module, line_number, -1, "#ELSE_IF Found not in #IF statement!");
                        break;
                    case Program.IfStatus.in_false_else_statement:
                    case Program.IfStatus.in_true_else_statement:
                        Program.Error(input_line, module, line_number, -1, "#ELSE_IF Found after an #ELSE statement!");
                        break;
                    case Program.IfStatus.in_true_if_statement:
                        Program.if_status = Program.IfStatus.resolved_if_chain;
                        break;
                    case Program.IfStatus.in_false_if_statement:
                        // Sets the if_status value based on the label //
                        if (Program.CheckIfStatement(output, input_line, line_number, module))
                            Program.if_status = Program.IfStatus.in_false_if_statement;
                        else
                            Program.if_status = Program.IfStatus.in_true_if_statement;
                        break;
                }
                return (Program.Statement)null;
            }

            // #ELSE is just do the opposite //
            if (output.instruction == "#ELSE")
            {
                if (output.tokens.Count > 0)
                    Program.Error(input_line, module, line_number, -1, "Too many tokens for #ELSE statement!");
                switch (Program.if_status)
                {
                    case Program.IfStatus.not_in_if_block:
                        Program.Error(input_line, module, line_number, -1, "#ELSE Found not in #IF statement!");
                        break;
                    case Program.IfStatus.in_false_else_statement:
                    case Program.IfStatus.in_true_else_statement:
                        Program.Error(input_line, module, line_number, -1, "#ELSE Found after an #ELSE statement!");
                        break;
                    // #ELSE is just do the opposite //
                    case Program.IfStatus.in_true_if_statement:
                    case Program.IfStatus.resolved_if_chain:
                        Program.if_status = Program.IfStatus.in_false_else_statement;
                        break;
                    case Program.IfStatus.in_false_if_statement:
                        Program.if_status = Program.IfStatus.in_true_else_statement;
                        break;
                }

                return (Program.Statement)null;
            }

            // Turn off the #IF flag //
            if (output.instruction == "#END_IF")
            {
                if (output.tokens.Count > 0)
                    Program.Error(input_line, module, line_number, -1, "Too many tokens for #END_IF statement!");
                if (Program.if_status == Program.IfStatus.not_in_if_block)
                    Program.Error(input_line, module, line_number, -1, "#END_IF Found not in #IF statement!");

                Program.if_status = Program.IfStatus.not_in_if_block;
                return (Program.Statement)null;
            }

            // If we are in any of the statuses that are false, skip this line //
            if (Program.if_status == Program.IfStatus.in_false_if_statement || Program.if_status == Program.IfStatus.in_false_else_statement || Program.if_status == Program.IfStatus.resolved_if_chain)
            {
                return (Program.Statement)null;
            }

            // Set a value for the IF check
            if (output.instruction == "#SET")
            {
                Program.AddSetting(output, input_line, line_number, module);
                return (Program.Statement)null;
            }

            if (output.instruction == "#SYMBOL")
                Program.tokenize_symbol(input_line, line_number, statement_number, output, module);
            return output;
        }

        private static void tokenize_symbol(
          char[] input_line,
          int line_number,
          int statement_number,
          Program.Statement output,
          string module)
        {
            if (output.tokens.Count == 2)
            {
                if (output.tokens[0].parse_type != Program.ParseType.name)
                    Program.Error(input_line, module, line_number, -1, output.tokens[0].raw_string + " is not a valid " + output.instruction + " name");
                long num = -1;
                uint size = 4;
                switch (output.tokens[1].parse_type)
                {
                    case Program.ParseType.integer_number:
                        num = long.Parse(output.tokens[1].raw_string, (IFormatProvider)CultureInfo.InvariantCulture);
                        size = 4U;
                        break;
                    case Program.ParseType.float_number:
                        num = (long)BitConverter.ToUInt32(BitConverter.GetBytes(float.Parse(output.tokens[1].raw_string, (IFormatProvider)CultureInfo.InvariantCulture)), 0);
                        size = 4U;
                        break;
                    case Program.ParseType.hex_number:
                        num = Convert.ToInt64(output.tokens[1].raw_string, 16);
                        size = (uint)(output.tokens[1].raw_string.Length - 2) / 2U;
                        break;
                    case Program.ParseType.expression:
                        Program.assign_value_to_expression(output, output.tokens[1]);
                        num = output.tokens[1].value;
                        size = 4U;
                        break;
                    default:
                        Program.Error(input_line, module, line_number, -1, output.tokens[1].raw_string + " is not a valid " + output.instruction + " integer or float number");
                        break;
                }
                Program.add_symbol(output.tokens[0].raw_string, num, Program.SymbolType.from_symbol_directive, input_line, line_number, -1, statement_number, size, module);
            }
            else
                Program.Error(input_line, module, line_number, -1, output.tokens.Count.ToString() + " is wrong number of inputs for " + output.instruction + " directive, need a name and a value");
        }

        private static bool find_token(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index)
        {
            while (index < input_line.Length)
            {
                switch (input_line[index])
                {
                    case '\t':
                    case '\n':
                    case '\r':
                    case ' ':
                    case ',':
                        ++index;
                        continue;
                    case ';':
                        return false;
                    default:
                        return true;
                }
            }
            return false;
        }

        private static Program.Token ReadArgument(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            switch (input_line[index])
            {
                case '"':
                    return Program.ReadString(input_line, line_number, statement_number, ref index, module);
                case '#':
                case '.':
                case '/':
                case '_':
                    return Program.ReadSymbol(input_line, line_number, statement_number, ref index, module);
                case '-':
                case '0':
                case '1':
                case '2':
                case '3':
                case '4':
                case '5':
                case '6':
                case '7':
                case '8':
                case '9':
                    return Program.ReadNumber(input_line, line_number, statement_number, ref index, module);
                case '@':
                    return Program.ReadIndirect(input_line, line_number, statement_number, ref index, module);
                case '{':
                    return Program.ReadExpression(input_line, line_number, statement_number, ref index, module);
                default:
                    if (char.IsLetter(input_line[index]))
                        return Program.ReadSymbol(input_line, line_number, statement_number, ref index, module);
                    Program.Error(input_line, module, line_number, index, "unidentified token starts with '" + input_line[index].ToString() + "' (this may be an assembler bug)");
                    return (Program.Token)null;
            }
        }

        private static Program.Token ReadExpression(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            if (Program.expression_table == null)
                Program.expression_table = new List<Program.Expression>();
            Program.Token token = new Program.Token();
            StringBuilder stringBuilder = new StringBuilder();
            token.parse_type = Program.ParseType.expression;
            if (input_line[index] != '{')
            {
                Program.Error(input_line, module, line_number, index, "Assembler bug? Thought I was reading an expression but it started with: " + input_line[index].ToString());
                return (Program.Token)null;
            }
            stringBuilder.Append('{');
            ++index;
            Program.Expression expression = new Program.Expression();
            expression.subtokens = new List<string>();
            expression.subtokens_type = new List<Program.Expression.SubtokenType>();
            token.expression = expression;
            bool flag = true;
            while (flag && index < input_line.Length)
            {
                char c = input_line[index];
                switch (c)
                {
                    case '\t':
                    case ' ':
                        stringBuilder.Append(c);
                        break;
                    case '(':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.open_parenthesis);
                        stringBuilder.Append(c);
                        break;
                    case ')':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.close_parenthesis);
                        stringBuilder.Append(c);
                        break;
                    case '*':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.multiply);
                        stringBuilder.Append(c);
                        break;
                    case '+':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.add);
                        stringBuilder.Append(c);
                        break;
                    case '-':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.subtract);
                        stringBuilder.Append(c);
                        break;
                    case '.':
                    case '_':
                        stringBuilder.Append(Program.ReadExpressionSubtoken(input_line, line_number, statement_number, ref index, module, token));
                        stringBuilder.Append(c);
                        break;
                    case '/':
                        expression.subtokens.Add(c.ToString());
                        expression.subtokens_type.Add(Program.Expression.SubtokenType.divide);
                        stringBuilder.Append(c);
                        break;
                    case '}':
                        flag = false;
                        stringBuilder.Append(c);
                        break;
                    default:
                        if (char.IsLetterOrDigit(c))
                        {
                            stringBuilder.Append(Program.ReadExpressionSubtoken(input_line, line_number, statement_number, ref index, module, token));
                            break;
                        }
                        Program.Error(input_line, module, line_number, index, "unexpected character '" + c.ToString() + "' " + stringBuilder.ToString());
                        break;
                }
                ++index;
            }
            if (flag)
            {
                Program.Error(input_line, module, line_number, index, "unclosed expression starting with " + stringBuilder.ToString());
                return (Program.Token)null;
            }
            token.raw_string = stringBuilder.ToString();
            Program.expression_table.Add(expression);
            return token;
        }

        private static string ReadExpressionSubtoken(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module,
          Program.Token token)
        {
            StringBuilder stringBuilder = new StringBuilder();
            bool flag = true;
            if (char.IsDigit(input_line[index]))
            {
                if (index + 1 < input_line.Length && input_line[index] == '0' && (input_line[index + 1] == 'x' || input_line[index + 1] == 'X'))
                {
                    token.expression.subtokens_type.Add(Program.Expression.SubtokenType.hex_number);
                    stringBuilder.Append(input_line[index]);
                    ++index;
                    stringBuilder.Append(input_line[index]);
                    ++index;
                    while (index < input_line.Length & flag)
                    {
                        char c = input_line[index];
                        if (char.IsDigit(c))
                        {
                            stringBuilder.Append(c);
                            ++index;
                        }
                        else if (c >= 'a' && c <= 'f' || c >= 'A' && c <= 'F')
                        {
                            stringBuilder.Append(c);
                            ++index;
                        }
                        else
                        {
                            --index;
                            flag = false;
                        }
                    }
                }
                else
                {
                    token.expression.subtokens_type.Add(Program.Expression.SubtokenType.decimal_number);
                    while (index < input_line.Length & flag)
                    {
                        char c = input_line[index];
                        if (char.IsDigit(c))
                        {
                            stringBuilder.Append(c);
                            ++index;
                        }
                        else
                        {
                            --index;
                            flag = false;
                        }
                    }
                }
            }
            else
            {
                token.expression.subtokens_type.Add(Program.Expression.SubtokenType.name);
                while (index < input_line.Length & flag)
                {
                    char c = input_line[index];
                    if (char.IsLetterOrDigit(c) || c == '.' || c == '_')
                    {
                        stringBuilder.Append(c);
                        ++index;
                    }
                    else
                    {
                        --index;
                        flag = false;
                    }
                }
            }
            string str = stringBuilder.ToString();
            token.expression.subtokens.Add(str);
            return str;
        }

        // Add a setting to the dictionary //
        private static void AddSetting(
            Program.Statement statement,
              char[] input_line,
              int line_number,
              string module)
        {
            int numTokens = statement.tokens.Count;

            if (numTokens != 2)
                Program.Error(input_line, module, line_number, -1, "ADD SETTING: REQUIRES 2 TOKENS [#SET LABEL VALUE]");

            string label = statement.tokens[0].raw_string.ToUpper();
            string sValue = statement.tokens[1].raw_string.ToUpper();
            string padding = new string(' ', label.Length < 20 ? 20 - label.Length : 0);
            long value;

            // Convert the string to the value //
            if (sValue == "TRUE")
                value = 1;
            // Catch the common 0 value strings //
            else if (sValue == "FALSE" || sValue == "0" || sValue == "0X00" || sValue == "0X0")
                value = 0;
            else
            {
                // Looks like it is something else, use built in function to set value //
                Program.assign_value_to_token(statement, statement.tokens[1]);
                value = statement.tokens[1].value;

                // Format the string for output
                if (value > 0)
                    sValue = $"0x{value:X2}";
                // If value was not valueable
                else
                    Program.Error(input_line, module, line_number, -1, $"ADD SETTING ERROR: Invalid value '{sValue}' [Use: 'True', 'False', or hex values]!");
            }

            // Check for updates //
            if (setting_dict.ContainsKey(label))
            {
                if (setting_dict[label] != value)
                {
                    setting_dict[label] = value;
                    System.Console.WriteLine($"UPDATE SETTING: {label}{padding} VALUE: {sValue}");
                }
            }
            else
            {
                setting_dict.Add(label, value);
                System.Console.WriteLine($" USING SETTING: {label}{padding} VALUE: {sValue}");
            }

            return;
        }

        // Check if the IF/NOT_IF value is set, returns the value for the 'in_false_if_statement' program flag //
        // Should be in this format #IF [#AND/#OR/#NOT] LABEL_1 LABEL_2 0x03 LABEL_3 ...
        private static bool CheckIfStatement(
            Program.Statement statement,
              char[] input_line,
              int line_number,
              string module)
        {
            int x;
            string label;
            string line = new string(statement.raw_line);
            int numTokens = statement.tokens.Count;
            bool orCheck = false;
            bool andCheck = false;
            bool notCheck = false;
            bool thisCheck = false;
            int settingsFound = 0;
            string nextLabel;

            if (numTokens == 0)
                Program.Error(input_line, module, line_number, -1, "CHECK IF: MISSING TOKEN");

            for (x = 0; x < numTokens; x++)
            {
                label = statement.tokens[x].raw_string.ToUpper();

                if (label == "#NOT")
                    notCheck = true;
                else if (label == "#AND")
                    andCheck = true;
                else if (label == "#OR")
                    orCheck = true;
                else if (Program.setting_dict.ContainsKey(label))
                    settingsFound++;
            }

            if (orCheck && andCheck)
                Program.Error(input_line, module, line_number, -1, "CHECK IF: #AND not compatible with #OR");

            if (settingsFound > 1 && !orCheck && !andCheck)
                Program.Error(input_line, module, line_number, -1, $"CHECK IF: Too many settings ");

            if (settingsFound == 0)
                Program.Error(input_line, module, line_number, -1, $"CHECK IF: No settings found in line '{line}'");


            // System.Console.WriteLine($"LINE: {line} ({statement.raw_line.Length}) token:{numTokens} settings: {settingsFound} not:{notCheck} and:{andCheck} or:{orCheck}");
            for (x = 0; x < numTokens; x++)
            {
                // Get the label //
                label = statement.tokens[x].raw_string.ToUpper();

                // Skip special tokens //
                if (label == "#NOT" || label == "#AND" || label == "#OR")
                    continue;

                //System.Console.WriteLine($"   x:{x} : {label}");

                if (Program.setting_dict.ContainsKey(label))
                {
                    if (numTokens > x + 1)
                    {
                        nextLabel = statement.tokens[x + 1].raw_string.ToUpper();

                        // Next token is setting, eval this token as bool
                        if (Program.setting_dict.ContainsKey(nextLabel))
                        {
                            if (setting_dict[label] == 1)
                                thisCheck = true;
                            else if (setting_dict[label] == 0)
                                thisCheck = false;
                            else
                                Program.Error(input_line, module, line_number, -1, $"IF CHECK: {label} not True/False {setting_dict[label]} (1)");
                        }
                        else
                        {
                            // Eval the next token //
                            Program.assign_value_to_token(statement, statement.tokens[x + 1]);
                            thisCheck = statement.tokens[x + 1].value == setting_dict[label];

                            // Skip the next token since it was evaluated already //
                            x++;
                        }
                    }
                    // Last token, eval this as normal
                    else
                    {
                        if (setting_dict[label] == 1)
                            thisCheck = true;
                        else if (setting_dict[label] == 0)
                            thisCheck = false;
                        else
                            Program.Error(input_line, module, line_number, -1, $"IF CHECK: {label} not True/False {setting_dict[label]} (2)");
                    }

                    // Only doing one check //
                    if (!orCheck && !andCheck)
                    {
                        return (notCheck ? thisCheck : !thisCheck);
                    }
                    // Any True OR check, leave //
                    else if (orCheck && thisCheck)
                    {
                        return notCheck;
                    }
                    // And False AND check, leave //
                    else if (andCheck && !thisCheck)
                    {
                        return !notCheck;
                    }
                }
                else
                    Program.Error(input_line, module, line_number, -1, $"IF CHECK: {label} NOT 'SET'");
            }

            // If we got here NONE of the ORs were found - Turn flag off //
            if (orCheck)
                thisCheck = false;
            // Or none of the AND checks were false - Turn flag on //
            else
                thisCheck = true;

            return (notCheck ? thisCheck : !thisCheck);
        }

        private static Program.Token ReadString(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            Program.Token token = new Program.Token();
            StringBuilder stringBuilder = new StringBuilder();
            token.parse_type = Program.ParseType.string_data;
            if (input_line[index] != '"')
            {
                Program.Error(input_line, module, line_number, index, "Assembler bug? Thought I was reading a string but it started with: " + input_line[index].ToString());
                return (Program.Token)null;
            }
            ++index;
            bool flag = true;
            while (flag && index < input_line.Length)
            {
                char ch1 = input_line[index];
                switch (ch1)
                {
                    case '"':
                        flag = false;
                        break;
                    case '\\':
                        ++index;
                        if (index < input_line.Length)
                        {
                            char ch2 = input_line[index];
                            switch (ch2)
                            {
                                case '"':
                                case '\\':
                                    stringBuilder.Append(ch2);
                                    break;
                                case 'n':
                                    stringBuilder.Append('\n');
                                    break;
                                case 't':
                                    stringBuilder.Append('\t');
                                    break;
                                default:
                                    Program.Error(input_line, module, line_number, index, "unknown escape sequence '\\" + input_line[index].ToString() + "'. if you wanted literally the \\ followed by that letter, escape it with \\\\");
                                    return (Program.Token)null;
                            }
                        }
                        else
                            break;
                        break;
                    default:
                        stringBuilder.Append(ch1);
                        break;
                }
                ++index;
            }
            if (flag)
            {
                Program.Error(input_line, module, line_number, index, "unclosed string starting with " + stringBuilder.ToString());
                return (Program.Token)null;
            }
            token.raw_string = stringBuilder.ToString();
            return token;
        }

        private static Program.Token ReadIndirect(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            if (index >= input_line.Length - 1)
            {
                Program.Error(input_line, module, line_number, index, "expected indirect, found end of line");
                return (Program.Token)null;
            }
            if (input_line[index] != '@')
            {
                Program.Error(input_line, module, line_number, index, "attempted to read indirect or absolute address starting with non-'@'. this is probably a bug with the assembler, please let us know.");
                return (Program.Token)null;
            }
            ++index;
            if (char.IsNumber(input_line[index]) && index + 2 < input_line.Length && (input_line[index + 1] == 'x' || input_line[index + 1] == 'X'))
            {
                Program.Token token = Program.ReadNumber(input_line, line_number, statement_number, ref index, module);
                if (token.parse_type == Program.ParseType.hex_number)
                    token.parse_type = Program.ParseType.absolute_displacement_address;
                else
                    Program.Error(input_line, module, line_number, index, "absolute displacement address @" + token.raw_string + " needs to be a hex number,\nand doesn't seem to be (start with @0x, etc)");
                return token;
            }
            Program.Token token1 = new Program.Token();
            StringBuilder stringBuilder = new StringBuilder();
            bool flag1 = false;
            bool flag2 = false;
            if (input_line[index] == '(')
            {
                stringBuilder.Append('(');
                flag1 = true;
                flag2 = true;
                ++index;
            }
            else if (input_line[index] == '-')
            {
                stringBuilder.Append('-');
                token1.parse_type = Program.ParseType.register_indirect_pre_decrement;
                ++index;
            }
            bool flag3 = true;
            while (index < input_line.Length & flag3)
            {
                char c = input_line[index];
                switch (c)
                {
                    case '\t':
                    case ' ':
                        if (flag2 | flag1)
                        {
                            stringBuilder.Append(c);
                            ++index;
                            continue;
                        }
                        flag3 = false;
                        continue;
                    case '\n':
                    case '\r':
                    case ';':
                        if (flag2)
                            Program.Error(input_line, module, line_number, index, "Found unexpected whitespace when close parenthesis expected in indirect address");
                        flag3 = false;
                        continue;
                    case ')':
                        if (flag2)
                        {
                            stringBuilder.Append(c);
                            flag3 = false;
                            ++index;
                            flag2 = false;
                            continue;
                        }
                        Program.Error(input_line, module, line_number, index, "Found unexpected ')' inside an indirect address argument (starts with '@')");
                        continue;
                    case '+':
                        stringBuilder.Append(c);
                        flag3 = false;
                        ++index;
                        token1.parse_type = Program.ParseType.register_indirect_post_increment;
                        continue;
                    case ',':
                        if (flag1)
                        {
                            stringBuilder.Append(c);
                            flag2 = true;
                            ++index;
                            continue;
                        }
                        if (flag2)
                            Program.Error(input_line, module, line_number, index, "Found unexpected ',' when close parenthesis expected in indirect");
                        flag3 = false;
                        continue;
                    case ':':
                        Program.Error(input_line, module, line_number, index, "Found unexpected ':' inside an indirect address argument (starts with '@')");
                        flag3 = false;
                        continue;
                    case '@':
                        flag3 = false;
                        continue;
                    default:
                        if (char.IsDigit(c))
                        {
                            if (token1.inner_token2 != null)
                                Program.Error(input_line, module, line_number, index, "no indirect address argument accepts more than 2 values");
                            else if (token1.inner_token != null)
                                Program.Error(input_line, module, line_number, index, "no indirect address argument accepts numeric tokens for the 2nd value");
                            token1.parse_type = Program.ParseType.register_indirect_displacement;
                            token1.inner_token = Program.ReadNumber(input_line, line_number, statement_number, ref index, module);
                            stringBuilder.Append(token1.inner_token.raw_string);
                            flag1 = true;
                            continue;
                        }
                        if (char.IsLetter(c))
                        {
                            if (token1.inner_token2 != null)
                            {
                                Program.Error(input_line, module, line_number, index, "no indirect address argument accepts more than 2 values");
                                continue;
                            }
                            if (token1.inner_token != null)
                            {
                                token1.inner_token2 = Program.ReadSymbol(input_line, line_number, statement_number, ref index, module);
                                stringBuilder.Append(token1.inner_token2.raw_string);
                                flag1 = false;
                                flag2 = true;
                                switch (token1.inner_token2.parse_type)
                                {
                                    case Program.ParseType.register_direct:
                                        if (token1.inner_token.parse_type == Program.ParseType.register_direct)
                                        {
                                            token1.parse_type = Program.ParseType.register_indexed_indirect;
                                            continue;
                                        }
                                        if (token1.inner_token.parse_type == Program.ParseType.name || token1.inner_token.parse_type == Program.ParseType.integer_number || token1.inner_token.parse_type == Program.ParseType.hex_number)
                                        {
                                            token1.parse_type = Program.ParseType.register_indirect_displacement;
                                            continue;
                                        }
                                        continue;
                                    case Program.ParseType.gbr_register:
                                        if (token1.inner_token.parse_type == Program.ParseType.name || token1.inner_token.parse_type == Program.ParseType.integer_number || token1.inner_token.parse_type == Program.ParseType.hex_number)
                                        {
                                            token1.parse_type = Program.ParseType.gbr_indirect_displacement;
                                            continue;
                                        }
                                        if (token1.inner_token.parse_type == Program.ParseType.register_direct)
                                        {
                                            if (Program.symbol_table[token1.inner_token.raw_string.ToUpperInvariant()].value == 0L)
                                            {
                                                token1.parse_type = Program.ParseType.gbr_indirect_indexed;
                                                continue;
                                            }
                                            Program.Error(input_line, module, line_number, index, "can't index GBR with registers that aren't R0, was " + token1.inner_token.raw_string);
                                            continue;
                                        }
                                        Program.Error(input_line, module, line_number, index, "can't index or displace GBR with " + token1.inner_token.raw_string);
                                        continue;
                                    case Program.ParseType.pc_register:
                                        if (token1.inner_token.parse_type == Program.ParseType.name || token1.inner_token.parse_type == Program.ParseType.integer_number || token1.inner_token.parse_type == Program.ParseType.hex_number)
                                        {
                                            token1.parse_type = Program.ParseType.pc_displacement;
                                            continue;
                                        }
                                        if (token1.inner_token.parse_type == Program.ParseType.register_direct)
                                        {
                                            Program.Error(input_line, module, line_number, index, "can't index PC with registers. (" + token1.inner_token.raw_string + ")");
                                            continue;
                                        }
                                        Program.Error(input_line, module, line_number, index, "can't index or displace PC with " + token1.inner_token.raw_string);
                                        continue;
                                    default:
                                        continue;
                                }
                            }
                            else
                            {
                                token1.inner_token = Program.ReadSymbol(input_line, line_number, statement_number, ref index, module);
                                stringBuilder.Append(token1.inner_token.raw_string);
                                if (token1.parse_type != Program.ParseType.register_indirect_pre_decrement)
                                {
                                    if (token1.inner_token.parse_type == Program.ParseType.name)
                                    {
                                        token1.parse_type = Program.ParseType.pc_displacement;
                                        continue;
                                    }
                                    token1.parse_type = Program.ParseType.register_indirect_displacement;
                                    if (flag2)
                                    {
                                        flag1 = true;
                                        continue;
                                    }
                                    token1.parse_type = Program.ParseType.register_indirect;
                                    continue;
                                }
                                continue;
                            }
                        }
                        else
                        {
                            Program.Error(input_line, module, line_number, index, "was trying to read an indirect (starts with '@" + stringBuilder.ToString() + "'), but found this " + c.ToString() + " in the middle of it");
                            continue;
                        }
                }
            }
            token1.raw_string = stringBuilder.ToString();
            return token1;
        }
        private static Program.Token ReadNumber(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            Program.Token token = new Program.Token();
            StringBuilder stringBuilder = new StringBuilder();
            token.parse_type = Program.ParseType.integer_number;
            // Console.WriteLine()
            if (index < input_line.Length - 1 && input_line[index] == '0')
            {
                if (input_line[index + 1] == 'x')
                {
                    if (input_line.Length - 1 < 3)
                        Program.Error(input_line, module, line_number, index, "Invalid Hex");
                    token.parse_type = Program.ParseType.hex_number;
                    stringBuilder.Append(input_line[index]);
                    stringBuilder.Append(input_line[index + 1]);
                    index += 2;

                }
            }
            else if (index < input_line.Length - 1 && input_line[index] == '-')
            {
                if (index + 2 < input_line.Length - 1)
                {
                    if (input_line[index + 2] == 'x')
                    {
                        token.parse_type = Program.ParseType.hex_number;
                        stringBuilder.Append(input_line[index]);
                        stringBuilder.Append(input_line[index + 1]);
                        stringBuilder.Append(input_line[index + 2]);
                        index += 3;
                    }
                    else
                    {
                        stringBuilder.Append('-');
                        ++index;
                    }
                }

            }
            // Console.WriteLine()
            bool flag = true;

            while (index < input_line.Length & flag)
            {
                char ch = input_line[index];
                switch (ch)
                {
                    case '\t':
                    case '\n':
                    case '\r':
                    case ' ':
                    case '(':
                    case ')':
                    case ',':
                    case ';':
                    case '@':
                        flag = false;
                        continue;
                    case '.':
                        if (token.parse_type == Program.ParseType.integer_number)
                            token.parse_type = Program.ParseType.float_number;
                        stringBuilder.Append(ch);
                        ++index;
                        continue;

                    case '0':
                    case '1':
                    case '2':
                    case '3':
                    case '4':
                    case '5':
                    case '6':
                    case '7':
                    case '8':
                    case '9':
                        stringBuilder.Append(ch);
                        ++index;
                        continue;
                    case ':':
                        Program.Error(input_line, module, line_number, index, "Found a ':', but this doesn't appear to be a valid label? but instead a number?? ");
                        flag = false;
                        continue;
                    case 'A':
                    case 'B':
                    case 'C':
                    case 'D':
                    case 'E':
                    case 'F':
                    case 'a':
                    case 'b':
                    case 'c':
                    case 'd':
                    case 'e':
                    case 'f':
                        if (token.parse_type == Program.ParseType.hex_number)
                        {
                            stringBuilder.Append(ch);
                            ++index;
                            continue;
                        }
                        Program.Error(input_line, module, line_number, index, "hex numbers need 0x at the start, but there was a " + ch.ToString() + " in this number? ");
                        continue;
                    default:
                        Program.Error(input_line, module, line_number, index, "was trying to read a number in, but found this " + ch.ToString() + " in the middle of it");
                        continue;
                }
            }
            // token.raw_string = stringBuilder.ToString();
            token.raw_string = stringBuilder.ToString();
            if (token.parse_type == Program.ParseType.hex_number)
            {
                if (token.raw_string[0] == '-' && token.raw_string[2] == 'x')
                {
                    //Console.WriteLine(token.raw_string);
                    token.raw_string = token.raw_string.Substring(1);

                    //Console.WriteLine(token.raw_string);
                    int intValue = (Convert.ToInt16(token.raw_string, 16) * -1) & 0xFF;
                    token.raw_string = "0x" + intValue.ToString("X2");
                    //Console.WriteLine(tokSen.raw_string);

                }
            }
            return token;
        }

        private static Program.Token ReadSymbolOrLabel(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            Program.Token token = Program.ReadSymbol(input_line, line_number, statement_number, ref index, module);
            if (index < input_line.Length && input_line[index] == ':')
            {
                if (token.parse_type != Program.ParseType.name)
                    Program.Error(input_line, module, line_number, index, "Invalid label \"" + token.raw_string + "\"");

                // Don't add the symbol if we are are skip mode //
                if (Program.if_status != Program.IfStatus.in_false_if_statement && Program.if_status != Program.IfStatus.in_false_else_statement && Program.if_status != Program.IfStatus.resolved_if_chain)
                {
                    Program.add_symbol(token.raw_string, (long)statement_number, Program.SymbolType.label, input_line, line_number, index, statement_number, 4U, module);
                }
                ++index;
                token.parse_type = Program.ParseType.label_declaration;
                token.raw_string += ":";
            }
            return token;
        }

        private static Program.Token ReadSymbol(
          char[] input_line,
          int line_number,
          int statement_number,
          ref int index,
          string module)
        {
            Program.Token token = new Program.Token();
            StringBuilder stringBuilder = new StringBuilder();
            token.parse_type = Program.ParseType.name;
            if (char.IsNumber(input_line[index]))
            {
                Program.Error(input_line, module, line_number, index, "Symbols, instructions, or label names cannot start with numbers, but started with " + input_line[index].ToString());
                return (Program.Token)null;
            }
            if (input_line[index] == '#')
            {
                stringBuilder.Append(input_line[index]);
                ++index;
            }
            bool flag = true;
            while (index < input_line.Length & flag)
            {
                char c = input_line[index];
                if (char.IsLetterOrDigit(c) || c == '.' || c == '/' || c == '_')
                {
                    stringBuilder.Append(c);
                    ++index;
                }
                else
                    flag = false;
            }
            token.raw_string = stringBuilder.ToString();
            string upperInvariant = token.raw_string.ToUpperInvariant();
            if (Program.symbol_table.ContainsKey(upperInvariant))
            {
                Program.Symbol symbol = Program.symbol_table[upperInvariant];
                if (symbol.symbol_type == Program.SymbolType.register)
                {
                    token.value = symbol.value;
                    switch (symbol.register_type)
                    {
                        case Program.RegisterType.r:
                            token.parse_type = Program.ParseType.register_direct;
                            break;
                        case Program.RegisterType.fr:
                            token.parse_type = Program.ParseType.fr_register_direct;
                            break;
                        case Program.RegisterType.dr:
                            token.parse_type = Program.ParseType.dr_register_direct;
                            break;
                        case Program.RegisterType.xd:
                            token.parse_type = Program.ParseType.xd_register_direct;
                            break;
                        case Program.RegisterType.fv:
                            token.parse_type = Program.ParseType.fv_register_direct;
                            break;
                        case Program.RegisterType.pc:
                            token.parse_type = Program.ParseType.pc_register;
                            break;
                        case Program.RegisterType.gbr:
                            token.parse_type = Program.ParseType.gbr_register;
                            break;
                        case Program.RegisterType.r_bank:
                            token.parse_type = Program.ParseType.r_bank_register_direct;
                            break;
                        default:
                            token.parse_type = Program.ParseType.other_register;
                            break;
                    }
                }
            }
            return token;
        }

        private static void Error(
          char[] input_line,
          string module,
          int line_number,
          int char_index,
          string message)
        {
            Program.Warn(input_line, module, line_number, char_index, message, "Error:\n");
            Console.WriteLine();
            Console.WriteLine();
            throw new Exception("NO ERROR HANDLING YET SORRY");
        }

        private static void Warn(
          char[] input_line,
          string module,
          int line_number,
          int char_index,
          string message,
          string prepend = "Warning:\n")
        {
            ++char_index;
            Console.Write(prepend);
            Console.WriteLine(message);
            if (char_index - 2 > 0)
            {
                Console.Write("\ton line number " + (object)line_number + " col " + (object)char_index);
                if (!string.IsNullOrEmpty(module))
                {
                    Console.Write(" in module ");
                    Console.Write(module);
                }
                Console.WriteLine(":");
                Console.WriteLine(input_line);
                StringBuilder stringBuilder = new StringBuilder();
                stringBuilder.Append(' ', char_index - 2);
                Console.WriteLine(stringBuilder.ToString() + "^");
                Console.WriteLine(stringBuilder.ToString() + "|");
                Console.WriteLine(stringBuilder.ToString() + "|");
            }
            else
            {
                Console.Write("\ton line number " + (object)line_number);
                if (!string.IsNullOrEmpty(module))
                {
                    Console.Write(" in module ");
                    Console.Write(module);
                }
                Console.WriteLine(":");
                Console.WriteLine(input_line);
            }
            Console.WriteLine();
            Console.WriteLine();
            throw new Exception("NO ERROR HANDLING YET SORRY");
        }

        private static void handle_module_loading(
          List<Program.Statement> input,
          List<Program.Statement> output,
          int statement_offset)
        {
            foreach (Program.Statement statement in input)
            {
                if (statement.instruction == "#MODULE")
                {
                    if (statement.tokens[0].parse_type == Program.ParseType.string_data)
                    {
                        string upperInvariant = Path.GetFileNameWithoutExtension(statement.tokens[0].raw_string).ToUpperInvariant();
                        if (Program.modules_loaded.ContainsKey(upperInvariant))
                            Program.Error((char[])null, statement.module, statement.line_number, 0, "");
                        string path = Path.Combine(Program.working_directory, statement.tokens[0].raw_string);
                        Program.modules_loaded.Add(upperInvariant, new Program.Module()
                        {
                            name = upperInvariant,
                            statement_number_offset = output.Count + statement_offset
                        });
                        using (StreamReader reader = File.OpenText(path))
                        {
                            List<Program.Statement> input1 = Program.tokenize_and_parse(reader, upperInvariant, output.Count + statement_offset);
                            List<Program.Statement> statementList = new List<Program.Statement>();
                            Program.handle_module_loading(input1, statementList, output.Count + statement_offset);
                            output.AddRange((IEnumerable<Program.Statement>)statementList);
                        }
                    }
                }
                else
                    output.Add(statement);
            }
        }

        private static void associate_labels(List<Program.Statement> statements)
        {
            foreach (Program.Symbol symbol in Program.symbol_table.Values)
            {
                if (!symbol.has_been_associated && symbol.symbol_type == Program.SymbolType.label && statements.Count > symbol.statement_number && symbol.statement_number >= 0)
                {
                    if (statements[symbol.statement_number].associated_labels == null)
                        statements[symbol.statement_number].associated_labels = new List<Program.Symbol>();
                    statements[symbol.statement_number].associated_labels.Add(symbol);
                    symbol.has_been_associated = true;
                }
            }
        }

        private static void fix_associated_labels(List<Program.Statement> statements)
        {
            for (int index = 0; index < statements.Count; ++index)
            {
                Program.Statement statement = statements[index];
                if (statement.associated_labels != null)
                {
                    foreach (Program.Symbol associatedLabel in statement.associated_labels)
                        associatedLabel.statement_number = index;
                }
            }
        }

        private enum ParseType
        {
            none,
            name,
            integer_number,
            float_number,
            hex_number,
            label_declaration,
            register_direct,
            fr_register_direct,
            fv_register_direct,
            dr_register_direct,
            xd_register_direct,
            r_bank_register_direct,
            register_indirect,
            register_indirect_post_increment,
            register_indirect_pre_decrement,
            register_indirect_displacement,
            register_indexed_indirect,
            gbr_register,
            gbr_indirect_displacement,
            gbr_indirect_indexed,
            pc_displacement,
            pc_register,
            other_register,
            string_data,
            absolute_displacement_address,
            expression,
        }

        private class Token
        {
            public string raw_string;
            public Program.ParseType parse_type;
            public Program.Token inner_token;
            public Program.Token inner_token2;
            public Program.Expression expression;
            public long value;
            public uint size;
            public bool is_value_assigned;
        }

        private class Statement
        {
            public char[] raw_line;
            public string instruction;
            public List<Program.Token> tokens;
            public int line_number;
            public uint address;
            public string module;
            public List<Program.Symbol> associated_labels;
            public int repeat_count;
        }

        private enum SymbolType
        {
            none,
            label,
            from_symbol_directive,
            builtin,
            instruction,
            register,
            alias,
        }

        private enum RegisterType
        {
            not_applicable,
            r,
            fr,
            dr,
            xd,
            fv,
            pc,
            gbr,
            r_bank,
            other,
        }

        private class Symbol
        {
            public string name;
            public string short_name;
            public string alias_target;
            public long value;
            public Program.SymbolType symbol_type;
            public Program.RegisterType register_type;
            public int line_number;
            public int statement_number;
            public uint address;
            public uint size;
            public string module;
            public bool has_been_associated;
        }

        private class Module
        {
            public string name;
            public int statement_number_offset;
        }

        private class Expression
        {
            public List<string> subtokens;
            public List<Program.Expression.SubtokenType> subtokens_type;

            public enum SubtokenType
            {
                add,
                subtract,
                multiply,
                divide,
                open_parenthesis,
                close_parenthesis,
                name,
                decimal_number,
                hex_number,
            }
        }

        private enum Endian
        {
            Little,
            Big,
        }

        private enum IfStatus
        {
            not_in_if_block,
            resolved_if_chain,
            in_false_if_statement,
            in_true_if_statement,
            in_false_else_statement,
            in_true_else_statement,
        }
    }
}
