Imports System.Globalization
Imports System.Net
Imports System.Net.Sockets
Imports System.Text
Imports System.Text.RegularExpressions
Imports System.Threading
Imports System.IO
Imports System.Data.SqlClient

Public Class Service1

    Private receivingClient As UdpClient
    Private sendingClient As UdpClient
    Private receivingThread As Thread
    Dim SQLServer As String = "localhost", Port As Integer = 514, SQLPW As String, SQLUN As String, SQLDB As String, SQLConnStr As String, lastExTS As Date = "9/1/1985"
    Dim sqlRetryCount As Integer = 0

    Protected Overrides Sub OnStart(ByVal args() As String)

        If Not EventLog.SourceExists("Syslog2SQL") Then EventLog.CreateEventSource("Syslog2SQL", "Application")

        Try

            Dim help As String = "Syslog2SQL, Ver. 1.0 (4/2/2021), toddnelson.net.  https://toddnelson.net" & vbCrLf & vbCrLf &
            "Place a syslog2sql.cfg file in the same directory as Syslog2SQL.exe with the following settings: " & vbCrLf & vbCrLf &
            "sqlserver=<hostname/IP address of your MS SQL server> (default: localhost)" & vbCrLf &
            "sqldatabase=<database name to use> (Required)" & vbCrLf &
            "port=<UDP port to listen on> (default: 514)" & vbCrLf &
            "sqlusername=<username to use to authenticate to SQL> (Required)" & vbCrLf &
            "sqlpassword=<password to use to authenticate to SQL> (Required)"

            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory)

            If Not File.Exists("syslog2sql.cfg") Then
                EventLog.WriteEntry("Syslog2SQL", help, EventLogEntryType.Error, 1, 1)
                [Stop]()
                Exit Sub
            End If

            For Each line As String In File.ReadLines("syslog2sql.cfg")
                If line.Substring(0, line.IndexOf("=") + 1) = "sqlserver=" And line.Length > 10 Then SQLServer = line.Substring(line.IndexOf("=") + 1)
                If line.Substring(0, line.IndexOf("=") + 1) = "port=" And line.Length > 5 Then Port = line.Substring(line.IndexOf("=") + 1)
                If line.Substring(0, line.IndexOf("=") + 1) = "sqlusername=" And line.Length > 12 Then SQLUN = line.Substring(line.IndexOf("=") + 1)
                If line.Substring(0, line.IndexOf("=") + 1) = "sqlpassword=" And line.Length > 12 Then SQLPW = line.Substring(line.IndexOf("=") + 1)
                If line.Substring(0, line.IndexOf("=") + 1) = "sqldatabase=" And line.Length > 12 Then SQLDB = line.Substring(line.IndexOf("=") + 1)
            Next line

            receivingClient = New UdpClient(Port)
            Dim start As ThreadStart = New ThreadStart(AddressOf Receiver)
            receivingThread = New Thread(start)
            receivingThread.IsBackground = True
            receivingThread.Start()

        Catch ex As Exception
            EventLog.WriteEntry("Syslog2SQL", "Exception Message:" & vbCrLf & ex.Message & vbCrLf & vbCrLf & "Exception Trace:" & vbCrLf & ex.StackTrace, EventLogEntryType.Error, 1, 1)
            [Stop]()
            Exit Sub
        End Try

    End Sub

    Protected Overrides Sub OnStop()
        ' Add code here to perform any tear-down necessary to stop your service.
    End Sub
    Sub Receiver()
        'https://stackoverflow.com/questions/16160550/visual-basic-udpclient-server-client-model

        Dim csb As New SqlConnectionStringBuilder
        csb.InitialCatalog = SQLDB
        csb.UserID = SQLUN
        csb.Password = SQLPW
        csb.DataSource = SQLServer

        SQLConnStr = csb.ConnectionString

        Dim query As String = "IF OBJECT_ID('dbo.Syslog2SQL', 'U') IS NULL "
        query += "BEGIN "
        query += "CREATE TABLE [dbo].[Syslog2SQL]("
        query += "[id] INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Syslog2SQL PRIMARY KEY,"
        query += "[Severity] VARCHAR(8) NOT NULL,"
        query += "[Timestamp] datetime2(2) NOT NULL,"
        query += "[Host] VARCHAR(30) NOT NULL,"
        query += "[Application] VARCHAR(20),"
        query += "[PID] VARCHAR(20),"
        query += "[MsgID] VARCHAR(20),"
        query += "[Message] VARCHAR(8000),"
        query += ")"
        query += " END"

10:
        If sqlRetryCount >= 4 Then
            EventLog.WriteEntry("Syslog2SQL", "Can't access the SQL server specified after 5 attempts, giving up!", EventLogEntryType.Error, 1, 1)
            [Stop]()
            Exit Sub
        End If

        Try

            'create sql table if it doesn't exist in specified db
            Using con As New SqlConnection(SQLConnStr)
                Using cmd As New SqlCommand(query)
                    cmd.Connection = con
                    con.Open()
                    cmd.ExecuteNonQuery()
                    con.Close()
                End Using
            End Using

        Catch ex As Exception
            EventLog.WriteEntry("Syslog2SQL", "Can't access the SQL server specified, will try again in 3 minutes." & vbCrLf & vbCrLf & "Exception Message:" & vbCrLf & ex.Message & vbCrLf & vbCrLf & "Exception Trace:" & vbCrLf & ex.StackTrace, EventLogEntryType.Warning, 1, 1)
            Thread.Sleep(180000)
            sqlRetryCount += 1
            GoTo 10
        End Try

        Dim endPoint As IPEndPoint = New IPEndPoint(IPAddress.Any, Port)

        While (True)
            Try

                Dim data() As Byte
                data = receivingClient.Receive(endPoint)
                Dim message As String = Encoding.ASCII.GetString(data).Replace(vbCr, " ").Replace(vbLf, " ")

                Dim bsdMatch = Regex.Match(message, "^<(\d+)>(.+ \d\d:\d\d:\d\d) (.+?) (.+)$")

                Dim msgDate As Date, severity As Integer, host As String = "", msg As String = "", app As String = "", pid As String = "", msgid As String = "", isValid As Boolean = False

                If bsdMatch.Success Then
                    'message is in BSD format
                    isValid = True
                    severity = bsdMatch.Groups(1).Value
                    msgDate = Date.ParseExact(bsdMatch.Groups(2).Value.Replace("  ", " ").Trim, "MMM d HH:mm:ss", CultureInfo.InvariantCulture)
                    host = bsdMatch.Groups(3).Value
                    msg = bsdMatch.Groups(4).Value

                Else
                    Dim ietfMatch = Regex.Match(message, "^<(\d+)>(\d+) (.+?) (.+?) (.+?) (.+?) (.+?) (.+?)$")

                    If ietfMatch.Success Then
                        'message is in IETF format
                        isValid = True
                        severity = ietfMatch.Groups(1).Value
                        msgDate = Date.Parse(ietfMatch.Groups(3).Value)
                        host = ietfMatch.Groups(4).Value
                        app = ietfMatch.Groups(5).Value
                        msg = ietfMatch.Groups(8).Value
                        pid = ietfMatch.Groups(6).Value
                        msgid = ietfMatch.Groups(7).Value
                    Else
                        WriteErrorToLog("Unknown message format: " & message)
                    End If
                End If

                If isValid Then
                    'got a valid message, write to DB
                    Dim sql As String = "insert into Syslog2SQL (Severity, Timestamp, Host, Application, PID, MsgID, Message) VALUES(@Severity, @Timestamp, @Host, @Application, @PID, @MsgID, @Message)"

                    Using con As New SqlConnection(SQLConnStr)
                        Dim cmd As New SqlCommand(sql, con)

                        cmd.Parameters.Add("@Severity", SqlDbType.VarChar, 8).Value = severity
                        cmd.Parameters.Add("@Timestamp", SqlDbType.DateTime2, 2).Value = msgDate
                        cmd.Parameters.Add("@Host", SqlDbType.VarChar, 30).Value = host
                        cmd.Parameters.Add("@Application", SqlDbType.VarChar, 20).Value = app
                        cmd.Parameters.Add("@PID", SqlDbType.VarChar, 20).Value = pid
                        cmd.Parameters.Add("@MsgID", SqlDbType.VarChar, 20).Value = msgid
                        cmd.Parameters.Add("@Message", SqlDbType.VarChar, 8000).Value = msg
                        con.Open()
                        cmd.ExecuteNonQuery()
                        con.Close()
                    End Using
                End If

            Catch ex As Exception
                WriteErrorToLog("Exception Message:" & vbCrLf & ex.Message & vbCrLf & vbCrLf & "Exception Trace:" & vbCrLf & ex.StackTrace)
            End Try

        End While

    End Sub

    Sub WriteErrorToLog(Message As String)

        'only write an event if it's been more than 2 minutes since last event, crude way to prevent flooding the event log
        If lastExTS < Date.Now.AddMinutes(-2) Then
            EventLog.WriteEntry("Syslog2SQL", Message, EventLogEntryType.Error, 1, 1)
            lastExTS = Date.Now
        End If

    End Sub

End Class
