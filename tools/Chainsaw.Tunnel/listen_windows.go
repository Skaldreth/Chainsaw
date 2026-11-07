package main

import (
	"context"
	"net"
	"syscall"

	"golang.org/x/sys/windows"
)

// soExclusiveAddrUse is SO_EXCLUSIVEADDRUSE (~SO_REUSEADDR), which x/sys does not name.
const soExclusiveAddrUse = ^windows.SO_REUSEADDR

// listenLoopback listens on 127.0.0.1 with a free port, held exclusively: on Windows, another program (also of the
// same user, with SO_REUSEADDR) cannot bind the same address and port while the helper lives. Chainsaw checks after
// each connection to the proxy that the helper still lives, so a connection always reached the helper.
func listenLoopback(ctx context.Context) (net.Listener, error) {
	config := net.ListenConfig{Control: func(_, _ string, raw syscall.RawConn) error {
		var setErr error
		err := raw.Control(func(fd uintptr) {
			setErr = windows.SetsockoptInt(windows.Handle(fd), windows.SOL_SOCKET, soExclusiveAddrUse, 1)
		})
		if err != nil {
			return err
		}
		return setErr
	}}
	return config.Listen(ctx, "tcp4", "127.0.0.1:0")
}
