using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace WindowsOtpManager;

sealed class NativeDatabase : IDisposable
{
    readonly IntPtr db;
    public NativeDatabase(string path)
    {
        int rc = SqliteNative.Open(path, out db, 2 | 4, IntPtr.Zero);
        if (rc != 0) { string reason = Marshal.PtrToStringUTF8(SqliteNative.Error(db)) ?? "SQLite error"; throw new InvalidOperationException("Could not open local account storage: " + reason); }
    }
    public void Exec(string sql)
    {
        int rc = SqliteNative.Exec(db, sql, IntPtr.Zero, IntPtr.Zero, out IntPtr error);
        if (error != IntPtr.Zero) SqliteNative.Free(error);
        if (rc != 0) throw new InvalidOperationException("Could not update local account storage.");
    }
    public Statement Prepare(string sql) { int rc = SqliteNative.Prepare(db, sql, -1, out IntPtr stmt, IntPtr.Zero); if (rc != 0) throw new InvalidOperationException("Could not read local account storage."); return new Statement(stmt); }
    public void Dispose() => SqliteNative.Close(db);
}

sealed class Statement : IDisposable
{
    readonly IntPtr stmt;
    public Statement(IntPtr stmt) => this.stmt = stmt;
    public void BindText(int index, string value) => Check(SqliteNative.BindText(stmt,index,value,-1,new IntPtr(-1)));
    public void BindInt(int index, int value) => Check(SqliteNative.BindInt(stmt,index,value));
    public void BindBlob(int index, byte[] value) => Check(SqliteNative.BindBlob(stmt,index,value,value.Length,new IntPtr(-1)));
    public bool StepRow() { int rc=SqliteNative.Step(stmt); if(rc==100)return true; if(rc==101)return false; throw new InvalidOperationException("Could not read local account storage."); }
    public void StepDone() { int rc=SqliteNative.Step(stmt); if(rc!=101)throw new InvalidOperationException("Could not update local account storage."); }
    public string Text(int index) { IntPtr p=SqliteNative.ColumnText(stmt,index); return p==IntPtr.Zero?"":Marshal.PtrToStringUTF8(p)??""; }
    public int Int(int index) => SqliteNative.ColumnInt(stmt,index);
    public byte[] Blob(int index) { IntPtr p=SqliteNative.ColumnBlob(stmt,index); int n=SqliteNative.ColumnBytes(stmt,index); var b=new byte[n]; if(n>0)Marshal.Copy(p,b,0,n); return b; }
    static void Check(int rc) { if(rc!=0)throw new InvalidOperationException("Could not write local account storage."); }
    public void Dispose() => SqliteNative.Finalize(stmt);
}

static class SqliteNative
{
    const string Dll="winsqlite3.dll";
    [DllImport(Dll,EntryPoint="sqlite3_open_v2",CallingConvention=CallingConvention.Cdecl)] internal static extern int Open([MarshalAs(UnmanagedType.LPUTF8Str)] string filename,out IntPtr db,int flags,IntPtr vfs);
    [DllImport(Dll,EntryPoint="sqlite3_close",CallingConvention=CallingConvention.Cdecl)] internal static extern int Close(IntPtr db);
    [DllImport(Dll,EntryPoint="sqlite3_errmsg",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr Error(IntPtr db);
    [DllImport(Dll,EntryPoint="sqlite3_exec",CallingConvention=CallingConvention.Cdecl)] internal static extern int Exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,IntPtr callback,IntPtr arg,out IntPtr error);
    [DllImport(Dll,EntryPoint="sqlite3_free",CallingConvention=CallingConvention.Cdecl)] internal static extern void Free(IntPtr p);
    [DllImport(Dll,EntryPoint="sqlite3_prepare_v2",CallingConvention=CallingConvention.Cdecl)] internal static extern int Prepare(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)] string sql,int bytes,out IntPtr stmt,IntPtr tail);
    [DllImport(Dll,EntryPoint="sqlite3_finalize",CallingConvention=CallingConvention.Cdecl)] internal static extern int Finalize(IntPtr stmt);
    [DllImport(Dll,EntryPoint="sqlite3_step",CallingConvention=CallingConvention.Cdecl)] internal static extern int Step(IntPtr stmt);
    [DllImport(Dll,EntryPoint="sqlite3_bind_text",CallingConvention=CallingConvention.Cdecl)] internal static extern int BindText(IntPtr stmt,int index,[MarshalAs(UnmanagedType.LPUTF8Str)] string value,int bytes,IntPtr destructor);
    [DllImport(Dll,EntryPoint="sqlite3_bind_int",CallingConvention=CallingConvention.Cdecl)] internal static extern int BindInt(IntPtr stmt,int index,int value);
    [DllImport(Dll,EntryPoint="sqlite3_bind_blob",CallingConvention=CallingConvention.Cdecl)] internal static extern int BindBlob(IntPtr stmt,int index,byte[] value,int bytes,IntPtr destructor);
    [DllImport(Dll,EntryPoint="sqlite3_column_text",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ColumnText(IntPtr stmt,int column);
    [DllImport(Dll,EntryPoint="sqlite3_column_int",CallingConvention=CallingConvention.Cdecl)] internal static extern int ColumnInt(IntPtr stmt,int column);
    [DllImport(Dll,EntryPoint="sqlite3_column_blob",CallingConvention=CallingConvention.Cdecl)] internal static extern IntPtr ColumnBlob(IntPtr stmt,int column);
    [DllImport(Dll,EntryPoint="sqlite3_column_bytes",CallingConvention=CallingConvention.Cdecl)] internal static extern int ColumnBytes(IntPtr stmt,int column);
}

static class WindowsDpapi
{
    [StructLayout(LayoutKind.Sequential)] struct DataBlob { public int Size; public IntPtr Data; }
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool CryptProtectData(ref DataBlob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out DataBlob output);
    [DllImport("crypt32.dll",SetLastError=true,CharSet=CharSet.Unicode)] static extern bool CryptUnprotectData(ref DataBlob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,out DataBlob output);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr memory);
    public static byte[] Protect(byte[] data)=>Transform(data,true);
    public static byte[] Unprotect(byte[] data)=>Transform(data,false);
    static byte[] Transform(byte[] data,bool protect)
    {
        IntPtr inputMemory=Marshal.AllocHGlobal(data.Length); Marshal.Copy(data,0,inputMemory,data.Length); var input=new DataBlob{Size=data.Length,Data=inputMemory}; DataBlob output=default;
        try
        {
            bool ok=protect?CryptProtectData(ref input,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output):CryptUnprotectData(ref input,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,1,out output);
            if(!ok)throw new Win32Exception(Marshal.GetLastWin32Error()); var result=new byte[output.Size]; Marshal.Copy(output.Data,result,0,result.Length); return result;
        }
        finally
        {
            if(output.Data!=IntPtr.Zero){for(int i=0;i<output.Size;i++)Marshal.WriteByte(output.Data,i,0);LocalFree(output.Data);}
            for(int i=0;i<data.Length;i++)Marshal.WriteByte(inputMemory,i,0); Marshal.FreeHGlobal(inputMemory);
        }
    }
}
