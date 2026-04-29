' ShareRibbon\Storage\ConversationRepository.vb
' 会话消息表 CRUD

Imports System.Data.SQLite

''' <summary>
''' 单条会话消息 DTO（用于加载历史会话）
''' </summary>
Public Class ConversationMessageDto
    Public Property Role As String
    Public Property Content As String
    Public Property CreateTime As String
End Class

''' <summary>
''' 会话消息 CRUD
''' </summary>
Public Class ConversationRepository

    ''' <summary>
    ''' 插入一条会话消息
    ''' </summary>
    Public Shared Sub InsertMessage(sessionId As String, role As String, content As String, Optional isCollected As Boolean = False, Optional appType As String = Nothing)
        OfficeAiDatabase.EnsureInitialized()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand(
                "INSERT INTO conversation (session_id, role, content, is_collected, app_type) VALUES (@sid, @role, @content, @collected, @app)", conn)
                cmd.Parameters.AddWithValue("@sid", sessionId)
                cmd.Parameters.AddWithValue("@role", role)
                cmd.Parameters.AddWithValue("@content", If(content, ""))
                cmd.Parameters.AddWithValue("@collected", If(isCollected, 1, 0))
                cmd.Parameters.AddWithValue("@app", If(appType, ""))
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' 更新消息收藏状态
    ''' </summary>
    Public Shared Sub SetCollected(conversationId As Long, isCollected As Boolean)
        OfficeAiDatabase.EnsureInitialized()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand("UPDATE conversation SET is_collected=@c WHERE id=@id", conn)
                cmd.Parameters.AddWithValue("@c", If(isCollected, 1, 0))
                cmd.Parameters.AddWithValue("@id", conversationId)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' 按 responseUuid 更新收藏（需通过 session_id + 最新 assistant 消息定位，简化实现：按 session 最后一条 assistant 更新）
    ''' 若调用方有 conversation_id 可直接用 SetCollected
    ''' </summary>
    Public Shared Sub SetLastAssistantCollected(sessionId As String, isCollected As Boolean)
        OfficeAiDatabase.EnsureInitialized()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand(
                "UPDATE conversation SET is_collected=@c WHERE id=(SELECT id FROM conversation WHERE session_id=@sid AND role='assistant' ORDER BY create_time DESC LIMIT 1)", conn)
                cmd.Parameters.AddWithValue("@c", If(isCollected, 1, 0))
                cmd.Parameters.AddWithValue("@sid", sessionId)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    ''' <summary>
    ''' 按会话 ID 获取该会话下所有消息（按 create_time 升序），用于加载历史会话到界面
    ''' </summary>
    Public Shared Function GetMessagesBySession(sessionId As String) As List(Of ConversationMessageDto)
        OfficeAiDatabase.EnsureInitialized()
        Dim list As New List(Of ConversationMessageDto)()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand(
                "SELECT role, content, create_time FROM conversation WHERE session_id=@sid ORDER BY create_time ASC", conn)
                cmd.Parameters.AddWithValue("@sid", sessionId)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        list.Add(New ConversationMessageDto With {
                            .Role = rdr.GetString(0),
                            .Content = If(rdr.IsDBNull(1), "", rdr.GetString(1)),
                            .CreateTime = If(rdr.IsDBNull(2), "", rdr.GetString(2))
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    Public Shared Function GetSessionListByAppType(appType As String, Optional limit As Integer = 50) As List(Of SessionListItem)
        OfficeAiDatabase.EnsureInitialized()
        Dim list As New List(Of SessionListItem)()
        Dim sql = "SELECT c.session_id, MIN(c.create_time) as first_time, MAX(c.create_time) as last_time, " &
                  "(SELECT content FROM conversation c2 WHERE c2.session_id = c.session_id AND c2.role = 'user' ORDER BY c2.create_time ASC LIMIT 1) as first_user_msg, " &
                  "(SELECT SUBSTR(content, 1, 200) FROM conversation c3 WHERE c3.session_id = c.session_id AND c3.role = 'assistant' ORDER BY c3.create_time ASC LIMIT 1) as snippet, " &
                  "MAX(c.app_type) as app_type " &
                  "FROM conversation c "
        If Not String.IsNullOrEmpty(appType) Then
            sql &= " WHERE (c.app_type = @app OR c.app_type = '' OR c.app_type IS NULL)"
        End If
        sql &= " GROUP BY c.session_id ORDER BY last_time DESC LIMIT @limit"
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand(sql, conn)
                If Not String.IsNullOrEmpty(appType) Then
                    cmd.Parameters.AddWithValue("@app", appType)
                End If
                cmd.Parameters.AddWithValue("@limit", limit)
                Using rdr = cmd.ExecuteReader()
                    While rdr.Read()
                        list.Add(New SessionListItem With {
                            .SessionId = rdr.GetString(0),
                            .FirstTime = If(rdr.IsDBNull(1), "", rdr.GetString(1)),
                            .LastTime = If(rdr.IsDBNull(2), "", rdr.GetString(2)),
                            .FirstUserMessage = If(rdr.IsDBNull(3), "会话", rdr.GetString(3)),
                            .Snippet = If(rdr.IsDBNull(4), "", rdr.GetString(4)),
                            .AppType = If(rdr.IsDBNull(5), "", rdr.GetString(5))
                        })
                    End While
                End Using
            End Using
        End Using
        Return list
    End Function

    Public Shared Sub DeleteSession(sessionId As String)
        OfficeAiDatabase.EnsureInitialized()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Using cmd As New SQLiteCommand("DELETE FROM conversation WHERE session_id=@sid", conn)
                cmd.Parameters.AddWithValue("@sid", sessionId)
                cmd.ExecuteNonQuery()
            End Using
        End Using
    End Sub

    Public Shared Function GetLastSessionId(appType As String) As String
        OfficeAiDatabase.EnsureInitialized()
        Using conn As New SQLiteConnection(OfficeAiDatabase.GetConnectionString())
            conn.Open()
            Dim sql = "SELECT session_id FROM conversation WHERE (app_type = @app OR app_type = '' OR app_type IS NULL) ORDER BY create_time DESC LIMIT 1"
            Using cmd As New SQLiteCommand(sql, conn)
                cmd.Parameters.AddWithValue("@app", appType)
                Dim obj = cmd.ExecuteScalar()
                If obj IsNot Nothing AndAlso Not IsDBNull(obj) Then
                    Return obj.ToString()
                End If
            End Using
        End Using
        Return Nothing
    End Function
End Class

Public Class SessionListItem
    Public Property SessionId As String
    Public Property FirstTime As String
    Public Property LastTime As String
    Public Property FirstUserMessage As String
    Public Property Snippet As String
    Public Property AppType As String
End Class
