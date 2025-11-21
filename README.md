# UnityCompilerTcpServer

This is a TCP server runs on Unity Editor. You can request to compile and earn syntax errors in the project like this:
```json
{
	"messages": [
		{
			"type":"Error",
			"message":"Assets\\Dodgeball\\Scripts\\AgentCubeGroundCheck.cs(14,94): error CS1585: Member modifier 'public' must precede the member type and name",
			"file":"Assets\\Dodgeball\\Scripts\\AgentCubeGroundCheck.cs",
			"line":14,
			"column":94
		}
	]
}{
	"messages": [
		{
			"type":"Error",
			"message":"Assets\\Dodgeball\\Scripts\\AgentCubeGroundCheck.cs(14,94): error CS1585: Member modifier 'public' must precede the member type and name",
			"file":"Assets\\Dodgeball\\Scripts\\AgentCubeGroundCheck.cs",
			"line":14,
			"column":94
		}
	]
}
```

## How to use
### Installing
Just copy [`CompilerServer.cs`](CompilerServer.cs) into your project.

### Usage
1. navigate `Window` -> `Compiler TCP Server`
<img width="627" height="292" alt="image" src="https://github.com/user-attachments/assets/c10a2ce1-4446-4f59-863f-ea6b03968d64" />

2. Run the server
<img width="724" height="460" alt="image" src="https://github.com/user-attachments/assets/11393c05-2bb5-4614-b208-ab0fdff9cfda" />

3. Send TCP request
Anything is OK with the content. It does not read the content.  
[This simple Python script](TestingClient.py) would help you


